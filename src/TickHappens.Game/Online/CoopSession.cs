using System.Buffers.Binary;
using TickHappens.Game.Sim;

namespace TickHappens.Game.Online;

public interface ICoopTransport
{
    event Action<string, ReadOnlyMemory<byte>>? PacketReceived;
    event Action<string>? PeerDisconnected;
    void Send(string peerId, ReadOnlySpan<byte> payload, bool reliable);
}

/// <summary>Versioned host/client game protocol over authenticated room members.</summary>
public sealed class CoopSession : IDisposable
{
    public const int MaxPlayers = 4;
    // Fits LiteNetLib's initial 1024-byte MTU including its reliable header.
    public const int MaxPacketBytes = 1000;
    public const int MaxSnapshotBytes = 256 * 1024;
    public const byte ProtocolVersion = 2;
    private const int HeaderBytes = 2;
    public const int MaxEventBytes = MaxPacketBytes - HeaderBytes;
    private const int SnapshotHeaderBytes = 12;
    private const int FragmentBytes = MaxPacketBytes - SnapshotHeaderBytes;
    private const int RedundantInputs = 3;

    private enum Message : byte { Hello = 1, Welcome, Rejected, Inputs, Snapshot, Heartbeat, Events }
    private enum Rejection : byte { Incompatible = 1, Full, Started }

    private readonly ICoopTransport _transport;
    private readonly TimeProvider _clock;
    private readonly string? _hostPeerId;
    private readonly byte[] _digest;
    private readonly string?[] _peerSlots = new string?[MaxPlayers];
    private readonly int[] _lastInput = new int[MaxPlayers];
    private readonly long[] _lastPeerActivity = new long[MaxPlayers];
    private readonly byte[] _inputHistory = new byte[RedundantInputs];
    private readonly byte[] _assembly = new byte[MaxSnapshotBytes];
    private readonly bool[] _received = new bool[(MaxSnapshotBytes + FragmentBytes - 1) / FragmentBytes];
    private int _inputSequence;
    private int _snapshotTick = -1, _snapshotLength, _receivedCount, _deliveredTick = -1;
    private long _joinStarted, _lastHello, _lastHeartbeat, _lastHostActivity;
    private bool _joining, _disposed;

    public CoopSession(ICoopTransport transport, string contentSha256, string? hostPeerId = null, TimeProvider? clock = null)
    {
        _digest = Convert.FromHexString(contentSha256);
        if (_digest.Length != 32) throw new ArgumentException("Content digest must be SHA-256.", nameof(contentSha256));
        _transport = transport;
        _clock = clock ?? TimeProvider.System;
        _hostPeerId = hostPeerId;
        LocalPlayerSlot = IsHost ? 0 : null;
        PlayerCount = IsHost ? 1 : 0;
        _transport.PacketReceived += Receive;
        _transport.PeerDisconnected += Disconnect;
    }

    public bool IsHost => _hostPeerId is null;
    public int? LocalPlayerSlot { get; private set; }
    public int PlayerCount { get; private set; }
    public bool AcceptNewPlayers { get; set; } = true;
    public string? JoinError { get; private set; }
    public event Action<int>? PeerJoined;
    public event Action<int>? PeerLeft;
    public event Action? HostDisconnected;
    public event Action<int, int, RunInput>? InputReceived;

    /// <summary>The snapshot buffer remains valid until the next snapshot starts arriving.</summary>
    public event Action<int, ReadOnlyMemory<byte>>? SnapshotReceived;
    public event Action<ReadOnlyMemory<byte>>? EventsReceived;

    public void BroadcastEvents(ReadOnlySpan<byte> events)
    {
        if (_disposed || !IsHost || events.IsEmpty || events.Length > MaxEventBytes) return;
        Span<byte> packet = stackalloc byte[HeaderBytes + events.Length];
        Header(packet, Message.Events);
        events.CopyTo(packet[HeaderBytes..]);
        for (int slot = 1; slot < MaxPlayers; slot++)
            if (_peerSlots[slot] is { } peer) _transport.Send(peer, packet, reliable: true);
    }

    public void Join()
    {
        if (_disposed || IsHost || LocalPlayerSlot.HasValue) return;
        JoinError = null;
        _joining = true;
        _joinStarted = _clock.GetTimestamp();
        SendHello(_joinStarted);
    }

    /// <summary>Drive control-plane timeouts independently of simulation ticks.</summary>
    public void Poll()
    {
        if (_disposed) return;
        long now = _clock.GetTimestamp();
        long second = _clock.TimestampFrequency;
        if (_joining && now - _joinStarted >= 15 * second)
        {
            _joining = false;
            JoinError = "online.timeout";
        }
        if (_joining && now - _lastHello >= second) SendHello(now);

        bool sendHeartbeat = now - _lastHeartbeat >= 2 * second;
        if (sendHeartbeat) _lastHeartbeat = now;
        if (IsHost)
        {
            for (int slot = 1; slot < MaxPlayers; slot++)
            {
                if (_peerSlots[slot] is not { } peer) continue;
                if (now - _lastPeerActivity[slot] >= 15 * second) Disconnect(peer);
                else if (sendHeartbeat) SendHeartbeat(peer);
            }
        }
        else if (LocalPlayerSlot.HasValue)
        {
            if (now - _lastHostActivity >= 15 * second) Disconnect(_hostPeerId!);
            else if (sendHeartbeat) SendHeartbeat(_hostPeerId!);
        }
    }

    private void SendHello(long now)
    {
        _lastHello = now;
        Span<byte> packet = stackalloc byte[HeaderBytes + 32];
        Header(packet, Message.Hello);
        _digest.CopyTo(packet[HeaderBytes..]);
        _transport.Send(_hostPeerId!, packet, reliable: true);
    }

    public void SubmitInput(RunInput input)
    {
        if (_disposed || IsHost || !LocalPlayerSlot.HasValue) return;
        int sequence = ++_inputSequence;
        int count = Math.Min(sequence, RedundantInputs);
        _inputHistory[sequence % RedundantInputs] = Pack(input);
        Span<byte> packet = stackalloc byte[6 + RedundantInputs];
        Header(packet, Message.Inputs);
        BinaryPrimitives.WriteInt32LittleEndian(packet[2..], sequence);
        for (int i = 0; i < count; i++)
            packet[6 + i] = _inputHistory[(sequence - count + 1 + i) % RedundantInputs];
        _transport.Send(_hostPeerId!, packet[..(6 + count)], reliable: false);
    }

    public void BroadcastSnapshot(int tick, ReadOnlySpan<byte> snapshot, bool reliable = false)
    {
        if (_disposed || !IsHost) return;
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (snapshot.IsEmpty || snapshot.Length > MaxSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        Span<byte> packet = stackalloc byte[MaxPacketBytes];
        Header(packet, Message.Snapshot);
        BinaryPrimitives.WriteInt32LittleEndian(packet[2..], tick);
        BinaryPrimitives.WriteInt32LittleEndian(packet[6..], snapshot.Length);
        for (int offset = 0; offset < snapshot.Length; offset += FragmentBytes)
        {
            int length = Math.Min(FragmentBytes, snapshot.Length - offset);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[10..], (ushort)(offset / FragmentBytes));
            snapshot.Slice(offset, length).CopyTo(packet[SnapshotHeaderBytes..]);
            for (int slot = 1; slot < MaxPlayers; slot++)
                if (_peerSlots[slot] is { } peer)
                    _transport.Send(peer, packet[..(SnapshotHeaderBytes + length)], reliable);
        }
    }

    private void Receive(string peer, ReadOnlyMemory<byte> memory)
    {
        var packet = memory.Span;
        if (_disposed || packet.Length is < HeaderBytes or > MaxPacketBytes) return;
        var message = (Message)packet[1];
        if (IsHost && message == Message.Hello)
        {
            ReceiveHello(peer, packet);
            return;
        }
        if (packet[0] != ProtocolVersion) return;
        if (IsHost)
        {
            int slot = Array.IndexOf(_peerSlots, peer, 1);
            if (slot > 0)
            {
                if (message == Message.Inputs) ReceiveInputs(slot, packet);
                if (message == Message.Heartbeat && packet.Length == HeaderBytes)
                    _lastPeerActivity[slot] = _clock.GetTimestamp();
            }
            return;
        }

        if (peer != _hostPeerId) return;
        switch (message)
        {
            case Message.Welcome when _joining && packet.Length == 3 && packet[2] is > 0 and < MaxPlayers:
                LocalPlayerSlot = packet[2];
                PlayerCount = 1;
                _lastHostActivity = _clock.GetTimestamp();
                _joining = false;
                break;
            case Message.Rejected when _joining && packet.Length == 3:
                JoinError = (Rejection)packet[2] switch
                {
                    Rejection.Full => "online.full",
                    Rejection.Started => "online.started",
                    _ => "online.incompatible",
                };
                _joining = false;
                break;
            case Message.Events when LocalPlayerSlot.HasValue && packet.Length > HeaderBytes:
                EventsReceived?.Invoke(memory[HeaderBytes..]);
                break;
            case Message.Snapshot when LocalPlayerSlot.HasValue:
                ReceiveSnapshot(packet);
                break;
            case Message.Heartbeat when LocalPlayerSlot.HasValue && packet.Length == HeaderBytes:
                _lastHostActivity = _clock.GetTimestamp();
                break;
        }
    }

    private void ReceiveHello(string peer, ReadOnlySpan<byte> packet)
    {
        if (packet.Length != HeaderBytes + _digest.Length) return;
        if (packet[0] != ProtocolVersion || !_digest.AsSpan().SequenceEqual(packet[HeaderBytes..]))
        {
            SendResponse(peer, Message.Rejected, (byte)Rejection.Incompatible, packet[0]);
            return;
        }
        int slot = Array.IndexOf(_peerSlots, peer, 1);
        if (slot < 0)
        {
            if (!AcceptNewPlayers)
            {
                SendResponse(peer, Message.Rejected, (byte)Rejection.Started, ProtocolVersion);
                return;
            }
            slot = Array.IndexOf(_peerSlots, null, 1);
            if (slot < 0)
            {
                SendResponse(peer, Message.Rejected, (byte)Rejection.Full, ProtocolVersion);
                return;
            }
            _peerSlots[slot] = peer;
            PlayerCount++;
            PeerJoined?.Invoke(slot);
        }
        _lastPeerActivity[slot] = _clock.GetTimestamp();
        SendResponse(peer, Message.Welcome, (byte)slot, ProtocolVersion);
    }

    private void ReceiveInputs(int slot, ReadOnlySpan<byte> packet)
    {
        if (packet.Length is < 7 or > 6 + RedundantInputs) return;
        int latest = BinaryPrimitives.ReadInt32LittleEndian(packet[2..]);
        if (latest <= _lastInput[slot]) return;
        var inputs = packet[6..];
        if (inputs.Length != Math.Min(latest, RedundantInputs)) return;
        foreach (byte packed in inputs)
            if (packed > 31 || (packed & 3) == 3 || ((packed >> 2) & 3) == 3) return;
        _lastPeerActivity[slot] = _clock.GetTimestamp();
        int first = latest - (inputs.Length - 1);
        for (int i = 0; i < inputs.Length; i++)
        {
            int sequence = first + i;
            if (sequence <= _lastInput[slot]) continue;
            _lastInput[slot] = sequence;
            byte packed = inputs[i];
            InputReceived?.Invoke(slot, sequence, new RunInput((packed & 3) - 1, ((packed >> 2) & 3) - 1, (packed & 16) != 0));
        }
    }

    private void ReceiveSnapshot(ReadOnlySpan<byte> packet)
    {
        if (packet.Length <= SnapshotHeaderBytes) return;
        int tick = BinaryPrimitives.ReadInt32LittleEndian(packet[2..]);
        int total = BinaryPrimitives.ReadInt32LittleEndian(packet[6..]);
        int index = BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]);
        if (tick < 0 || tick <= _deliveredTick || tick < _snapshotTick || total is <= 0 or > MaxSnapshotBytes) return;
        int offset = index * FragmentBytes;
        if (offset >= total || packet.Length != SnapshotHeaderBytes + Math.Min(FragmentBytes, total - offset)) return;
        _lastHostActivity = _clock.GetTimestamp();
        if (tick != _snapshotTick)
        {
            _snapshotTick = tick;
            _snapshotLength = total;
            _receivedCount = 0;
            Array.Clear(_received);
        }
        if (total != _snapshotLength || _received[index]) return;
        packet[SnapshotHeaderBytes..].CopyTo(_assembly.AsSpan(offset));
        _received[index] = true;
        if (++_receivedCount == (total + FragmentBytes - 1) / FragmentBytes)
        {
            _deliveredTick = tick;
            SnapshotReceived?.Invoke(tick, _assembly.AsMemory(0, total));
        }
    }

    private void Disconnect(string peer)
    {
        if (IsHost)
        {
            int slot = Array.IndexOf(_peerSlots, peer, 1);
            if (slot < 0) return;
            _peerSlots[slot] = null;
            _lastInput[slot] = 0;
            PlayerCount--;
            PeerLeft?.Invoke(slot);
        }
        else if (peer == _hostPeerId)
        {
            _joining = false;
            LocalPlayerSlot = null;
            PlayerCount = 0;
            JoinError = "online.hostClosed";
            HostDisconnected?.Invoke();
        }
    }

    private void SendResponse(string peer, Message message, byte value, byte wireVersion)
    {
        Span<byte> packet = [wireVersion, (byte)message, value];
        _transport.Send(peer, packet, reliable: true);
    }

    private void SendHeartbeat(string peer)
    {
        Span<byte> packet = stackalloc byte[HeaderBytes];
        Header(packet, Message.Heartbeat);
        _transport.Send(peer, packet, reliable: false);
    }

    private static void Header(Span<byte> packet, Message message)
    {
        packet[0] = ProtocolVersion;
        packet[1] = (byte)message;
    }

    private static byte Pack(RunInput input) =>
        (byte)((Math.Sign(input.X) + 1) | ((Math.Sign(input.Y) + 1) << 2) | (input.Dash ? 16 : 0));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.PacketReceived -= Receive;
        _transport.PeerDisconnected -= Disconnect;
    }
}
