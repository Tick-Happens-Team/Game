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
public sealed partial class CoopSession : IDisposable
{
    public const int MaxPlayers = 4;
    // Fits LiteNetLib's initial 1024-byte MTU including its reliable header.
    public const int MaxPacketBytes = 1000;
    public const int MaxSnapshotBytes = 256 * 1024;
    public const byte ProtocolVersion = 12;
    private const int HeaderBytes = 2;
    private const int RunHeaderBytes = HeaderBytes + sizeof(uint);
    public const int MaxEventBytes = MaxPacketBytes - RunHeaderBytes;
    private const int InputHeaderBytes = RunHeaderBytes + sizeof(int);
    private const int SnapshotHeaderBytes = RunHeaderBytes + 2 * sizeof(int) + sizeof(ushort);
    private const int FragmentBytes = MaxPacketBytes - SnapshotHeaderBytes;
    private const int RedundantInputs = 3;
    private const int DigestBytes = 32;
    private const int ResumeKeyBytes = 16;
    private const int ResumeSeconds = 60;

    private enum Message : byte { Hello = 1, Welcome, Rejected, Inputs, Snapshot, Heartbeat, Events, Keyframe, Profile, Roster, Ping, Pong, Latency, Pause, RunState, Ready }
    private enum Rejection : byte { Incompatible = 1, Full, Started, ResumeExpired }

    private readonly ICoopTransport _transport;
    private readonly TimeProvider _clock;
    private readonly string? _hostPeerId;
    private readonly byte[] _digest;
    private readonly Guid _resumeKey;
    private readonly bool _resume;
    private readonly Guid[] _slotKeys = new Guid[MaxPlayers];
    private readonly long[] _resumeDeadline = new long[MaxPlayers];
    private readonly string?[] _peerSlots = new string?[MaxPlayers];
    private readonly int[] _lastInput = new int[MaxPlayers];
    private readonly long[] _lastPeerActivity = new long[MaxPlayers];
    private readonly byte[] _inputHistory = new byte[RedundantInputs];
    private readonly SnapshotAssembly _snapshot = new();
    private readonly SnapshotAssembly _keyframe = new();
    private uint _runState; // Odd while a run is active, even in the lobby.
    private int _inputSequence;
    private long _joinStarted, _lastHello, _lastHeartbeat, _lastHostActivity;
    private bool _joining, _disposed;

    public CoopSession(ICoopTransport transport, string contentSha256, string? hostPeerId = null,
        TimeProvider? clock = null, Guid? resumeKey = null, bool resume = false, int characterCount = ushort.MaxValue + 1)
    {
        _digest = Convert.FromHexString(contentSha256);
        if (_digest.Length != DigestBytes) throw new ArgumentException("Content digest must be SHA-256.", nameof(contentSha256));
        _transport = transport;
        _clock = clock ?? TimeProvider.System;
        _hostPeerId = hostPeerId;
        _resumeKey = resumeKey ?? Guid.NewGuid();
        _resume = resume;
        _characterCount = characterCount;
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
    public event Action<int>? PeerResumed;
    public event Action<int>? PeerLeft;
    public event Action? HostDisconnected;
    internal event Action? RunStateChanged;
    public event Action<int, int, RunInput>? InputReceived;

    /// <summary>The snapshot buffer remains valid until the next snapshot starts arriving.</summary>
    public event Action<int, ReadOnlyMemory<byte>>? SnapshotReceived;
    public event Action<int, ReadOnlyMemory<byte>>? KeyframeReceived;
    public event Action<ReadOnlyMemory<byte>>? EventsReceived;

    public void BroadcastEvents(ReadOnlySpan<byte> events)
    {
        if (_disposed || !IsHost || events.IsEmpty || events.Length > MaxEventBytes) return;
        Span<byte> packet = stackalloc byte[RunHeaderBytes + events.Length];
        WriteRunHeader(packet, Message.Events);
        events.CopyTo(packet[RunHeaderBytes..]);
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
                else if (sendHeartbeat) SendPing(peer, slot, now);
            }
        }
        else if (LocalPlayerSlot.HasValue)
        {
            if (now - _lastHostActivity >= 15 * second)
            {
                Disconnect(_hostPeerId!);
                JoinError = "online.timeout";
            }
            else if (sendHeartbeat) SendPing(_hostPeerId!, 0, now);
        }
        if (IsHost && sendHeartbeat) BroadcastLatency();
    }

    private void SendHello(long now)
    {
        _lastHello = now;
        Span<byte> packet = stackalloc byte[HeaderBytes + DigestBytes + ResumeKeyBytes + 1];
        Header(packet, Message.Hello);
        _digest.CopyTo(packet[HeaderBytes..]);
        _resumeKey.TryWriteBytes(packet[(HeaderBytes + DigestBytes)..^1]);
        packet[^1] = _resume ? (byte)1 : (byte)0;
        _transport.Send(_hostPeerId!, packet, reliable: true);
    }

    public int SubmitInput(RunInput input)
    {
        if (_disposed || IsHost || !LocalPlayerSlot.HasValue) return 0;
        int sequence = ++_inputSequence;
        int count = Math.Min(sequence, RedundantInputs);
        _inputHistory[sequence % RedundantInputs] = Pack(input);
        Span<byte> packet = stackalloc byte[InputHeaderBytes + RedundantInputs];
        WriteRunHeader(packet, Message.Inputs);
        BinaryPrimitives.WriteInt32LittleEndian(packet[RunHeaderBytes..], sequence);
        for (int i = 0; i < count; i++)
            packet[InputHeaderBytes + i] = _inputHistory[(sequence - count + 1 + i) % RedundantInputs];
        _transport.Send(_hostPeerId!, packet[..(InputHeaderBytes + count)], reliable: false);
        return sequence;
    }

    public void BroadcastSnapshot(int tick, ReadOnlySpan<byte> snapshot, bool reliable = false) =>
        BroadcastState(Message.Snapshot, tick, snapshot, reliable);

    public void BroadcastKeyframe(int tick, ReadOnlySpan<byte> snapshot) =>
        BroadcastState(Message.Keyframe, tick, snapshot, reliable: true);

    private void BroadcastState(Message kind, int tick, ReadOnlySpan<byte> snapshot, bool reliable)
    {
        if (_disposed || !IsHost) return;
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (snapshot.IsEmpty || snapshot.Length > MaxSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        Span<byte> packet = stackalloc byte[MaxPacketBytes];
        WriteRunHeader(packet, kind);
        BinaryPrimitives.WriteInt32LittleEndian(packet[RunHeaderBytes..], tick);
        BinaryPrimitives.WriteInt32LittleEndian(packet[(RunHeaderBytes + sizeof(int))..], snapshot.Length);
        for (int offset = 0; offset < snapshot.Length; offset += FragmentBytes)
        {
            int length = Math.Min(FragmentBytes, snapshot.Length - offset);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[(SnapshotHeaderBytes - sizeof(ushort))..], (ushort)(offset / FragmentBytes));
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
                if (message == Message.Inputs && MatchesRun(packet)) ReceiveInputs(slot, packet);
                if (message == Message.Profile) ReceiveProfile(slot, packet);
                if (message == Message.Ready) ReceiveReady(slot, packet);
                ReceivePing(peer, slot, message, packet);
                if (message == Message.Heartbeat && packet.Length == HeaderBytes)
                    _lastPeerActivity[slot] = _clock.GetTimestamp();
            }
            return;
        }

        if (peer != _hostPeerId) return;
        if (LocalPlayerSlot.HasValue) ReceivePing(peer, 0, message, packet);
        switch (message)
        {
            case Message.Welcome when _joining && packet.Length == 3 && packet[2] is > 0 and < MaxPlayers:
                LocalPlayerSlot = packet[2];
                PlayerCount = 1;
                _lastHostActivity = _clock.GetTimestamp();
                _joining = false;
                if (_initialProfile is { } profile && !_resume) SendProfile(profile);
                break;
            case Message.Rejected when _joining && packet.Length == 3:
                JoinError = (Rejection)packet[2] switch
                {
                    Rejection.Full => "online.full",
                    Rejection.Started => "online.started",
                    Rejection.ResumeExpired => "online.resumeExpired",
                    _ => "online.incompatible",
                };
                _joining = false;
                break;
            case Message.RunState when LocalPlayerSlot.HasValue && packet.Length == RunHeaderBytes:
                uint state = BinaryPrimitives.ReadUInt32LittleEndian(packet[HeaderBytes..]);
                if (state > _runState)
                {
                    ApplyRunState(state);
                    RunStateChanged?.Invoke();
                }
                break;
            case Message.Events when LocalPlayerSlot.HasValue && packet.Length > RunHeaderBytes && MatchesRun(packet):
                EventsReceived?.Invoke(memory[RunHeaderBytes..]);
                break;
            case Message.Snapshot when LocalPlayerSlot.HasValue && MatchesRun(packet):
                ReceiveSnapshot(packet, _snapshot, SnapshotReceived);
                break;
            case Message.Keyframe when LocalPlayerSlot.HasValue && MatchesRun(packet):
                ReceiveSnapshot(packet, _keyframe, KeyframeReceived);
                break;
            case Message.Roster when LocalPlayerSlot.HasValue:
                ReceiveRoster(packet);
                break;
            case Message.Latency when LocalPlayerSlot.HasValue:
                ReceiveLatency(packet);
                break;
            case Message.Pause when LocalPlayerSlot.HasValue && packet.Length == 3 && packet[2] <= 1:
                Paused = packet[2] != 0;
                break;
            case Message.Heartbeat when LocalPlayerSlot.HasValue && packet.Length == HeaderBytes:
                _lastHostActivity = _clock.GetTimestamp();
                break;
        }
    }

    private void ReceiveHello(string peer, ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderBytes + DigestBytes) return;
        if (packet[0] != ProtocolVersion || !_digest.AsSpan().SequenceEqual(packet.Slice(HeaderBytes, DigestBytes)))
        {
            SendResponse(peer, Message.Rejected, (byte)Rejection.Incompatible, packet[0]);
            return;
        }
        if (packet.Length != HeaderBytes + DigestBytes + ResumeKeyBytes + 1 || packet[^1] > 1) return;
        var key = new Guid(packet[(HeaderBytes + DigestBytes)..^1]);
        bool requestedResume = packet[^1] == 1;
        if (key == Guid.Empty) return;
        long now = _clock.GetTimestamp();
        int slot = Array.IndexOf(_peerSlots, peer, 1);
        if (slot >= 0)
        {
            if (_slotKeys[slot] != key) return;
        }
        else
        {
            int reserved = Array.IndexOf(_slotKeys, key, 1);
            bool resume = requestedResume && reserved > 0 && _peerSlots[reserved] is null && now < _resumeDeadline[reserved];
            if (requestedResume && !resume)
            {
                SendResponse(peer, Message.Rejected, (byte)Rejection.ResumeExpired, ProtocolVersion);
                return;
            }
            if (!resume && (reserved > 0 || !AcceptNewPlayers))
            {
                SendResponse(peer, Message.Rejected, (byte)Rejection.Started, ProtocolVersion);
                return;
            }

            slot = resume ? reserved : Array.IndexOf(_peerSlots, null, 1);
            if (slot < 0)
            {
                SendResponse(peer, Message.Rejected, (byte)Rejection.Full, ProtocolVersion);
                return;
            }
            _peerSlots[slot] = peer;
            _present[slot] = true;
            _slotKeys[slot] = key;
            _resumeDeadline[slot] = 0;
            PlayerCount++;
            if (resume) PeerResumed?.Invoke(slot);
            else
            {
                _profiles[slot] = null;
                PeerJoined?.Invoke(slot);
            }
        }
        _lastPeerActivity[slot] = now;
        SendResponse(peer, Message.Welcome, (byte)slot, ProtocolVersion);
        if (_runState != 0)
        {
            Span<byte> state = stackalloc byte[RunHeaderBytes];
            WriteRunHeader(state, Message.RunState);
            _transport.Send(peer, state, reliable: true);
        }
        if (Paused) SendResponse(peer, Message.Pause, 1, ProtocolVersion);
        BroadcastRoster();
    }

    private void ReceiveInputs(int slot, ReadOnlySpan<byte> packet)
    {
        if (packet.Length is < InputHeaderBytes + 1 or > InputHeaderBytes + RedundantInputs) return;
        int latest = BinaryPrimitives.ReadInt32LittleEndian(packet[RunHeaderBytes..]);
        if (latest <= _lastInput[slot]) return;
        var inputs = packet[InputHeaderBytes..];
        if (inputs.Length != Math.Min(latest, RedundantInputs)) return;
        foreach (byte packed in inputs)
            if ((packed & 3) == 3 || ((packed >> 2) & 3) == 3) return;
        _lastPeerActivity[slot] = _clock.GetTimestamp();
        int first = latest - (inputs.Length - 1);
        for (int i = 0; i < inputs.Length; i++)
        {
            int sequence = first + i;
            if (sequence <= _lastInput[slot]) continue;
            _lastInput[slot] = sequence;
            byte packed = inputs[i];
            InputReceived?.Invoke(slot, sequence, new RunInput((packed & 3) - 1, ((packed >> 2) & 3) - 1, (packed & 16) != 0, (packed >> 5) & 3, (packed & 128) != 0));
        }
    }

    internal void SetRunActive(bool active)
    {
        if (!IsHost || ((_runState & 1) != 0) == active) return;
        ApplyRunState(_runState + 1);
        Span<byte> packet = stackalloc byte[RunHeaderBytes];
        WriteRunHeader(packet, Message.RunState);
        BroadcastControl(packet);
        if (!active) BroadcastRoster();
    }

    private void ApplyRunState(uint state)
    {
        _runState = state;
        AcceptNewPlayers = (state & 1) == 0;
        _readyMask = 0;
        Paused = false;
        _inputSequence = 0;
        Array.Clear(_lastInput);
        _snapshot.Reset();
        _keyframe.Reset();
    }

    // Unreliable packets can arrive after the reliable control message for the next run.
    private bool MatchesRun(ReadOnlySpan<byte> packet) =>
        packet.Length >= RunHeaderBytes && BinaryPrimitives.ReadUInt32LittleEndian(packet[HeaderBytes..]) == _runState;

    private void WriteRunHeader(Span<byte> packet, Message message)
    {
        Header(packet, message);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[HeaderBytes..], _runState);
    }

    private void ReceiveSnapshot(ReadOnlySpan<byte> packet, SnapshotAssembly assembly,
        Action<int, ReadOnlyMemory<byte>>? completed)
    {
        if (assembly.Receive(packet, completed))
            _lastHostActivity = _clock.GetTimestamp();
    }

    // Reliable keyframes and ordinary snapshots use independent fragment assembly.
    private sealed class SnapshotAssembly
    {
        private readonly byte[] _buffer = new byte[MaxSnapshotBytes];
        private readonly bool[] _received = new bool[(MaxSnapshotBytes + FragmentBytes - 1) / FragmentBytes];
        private int _tick = -1, _deliveredTick = -1, _length, _count;

        public void Reset() => _tick = _deliveredTick = -1;

        public bool Receive(ReadOnlySpan<byte> packet, Action<int, ReadOnlyMemory<byte>>? completed)
        {
            if (packet.Length <= SnapshotHeaderBytes) return false;
            int tick = BinaryPrimitives.ReadInt32LittleEndian(packet[RunHeaderBytes..]);
            int total = BinaryPrimitives.ReadInt32LittleEndian(packet[(RunHeaderBytes + sizeof(int))..]);
            int index = BinaryPrimitives.ReadUInt16LittleEndian(packet[(SnapshotHeaderBytes - sizeof(ushort))..]);
            if (tick <= _deliveredTick || tick < _tick || total is <= 0 or > MaxSnapshotBytes) return false;
            int offset = index * FragmentBytes;
            if (offset >= total || packet.Length != SnapshotHeaderBytes + Math.Min(FragmentBytes, total - offset)) return false;
            if (tick != _tick)
            {
                _tick = tick;
                _length = total;
                _count = 0;
                Array.Clear(_received);
            }
            if (total != _length || _received[index]) return false;
            packet[SnapshotHeaderBytes..].CopyTo(_buffer.AsSpan(offset));
            _received[index] = true;
            if (++_count == (total + FragmentBytes - 1) / FragmentBytes)
            {
                _deliveredTick = tick;
                completed?.Invoke(tick, _buffer.AsMemory(0, total));
            }
            return true;
        }
    }

    private void Disconnect(string peer)
    {
        if (IsHost)
        {
            int slot = Array.IndexOf(_peerSlots, peer, 1);
            if (slot < 0) return;
            _peerSlots[slot] = null;
            _present[slot] = false;
            _readyMask &= unchecked((byte)~(1 << slot));
            _pingId[slot] = 0;
            _latency[slot] = 0;
            _jitter[slot] = 0;
            if (AcceptNewPlayers) _profiles[slot] = null;
            _lastInput[slot] = 0;
            if (AcceptNewPlayers) _slotKeys[slot] = Guid.Empty;
            else _resumeDeadline[slot] = _clock.GetTimestamp() + ResumeSeconds * _clock.TimestampFrequency;
            PlayerCount--;
            PeerLeft?.Invoke(slot);
            BroadcastRoster();
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

    private static void Header(Span<byte> packet, Message message)
    {
        packet[0] = ProtocolVersion;
        packet[1] = (byte)message;
    }

    private static byte Pack(RunInput input) =>
        (byte)((Math.Sign(input.X) + 1) | ((Math.Sign(input.Y) + 1) << 2) | (input.Dash ? 16 : 0) | (Math.Clamp(input.ChestChoice, 0, 3) << 5) | (input.OpenChest ? 128 : 0));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.PacketReceived -= Receive;
        _transport.PeerDisconnected -= Disconnect;
    }
}
