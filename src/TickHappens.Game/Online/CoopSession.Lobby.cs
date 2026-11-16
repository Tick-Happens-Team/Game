using System.Buffers.Binary;
using System.Text;

namespace TickHappens.Game.Online;

public sealed partial class CoopSession
{
    internal const int NicknameLimit = 16;
    private const int ProfileBytes = 2 + NicknameLimit * 2;
    internal sealed record PlayerProfile(int Hero, string Nickname);
    private readonly int _characterCount;
    private readonly PlayerProfile?[] _profiles = new PlayerProfile?[MaxPlayers];
    private readonly bool[] _present = [true, false, false, false];
    private PlayerProfile? _initialProfile;
    private byte _readyMask;
    private readonly long[] _pingSent = new long[MaxPlayers], _lastPong = new long[MaxPlayers];
    private readonly uint[] _pingId = new uint[MaxPlayers];
    // Host: smoothed round trip to each guest (1 + ms, 0 = unknown); guests receive the host's table.
    private readonly ushort[] _latency = new ushort[MaxPlayers];
    private readonly double[] _jitter = new double[MaxPlayers];
    private readonly double[] _smoothedRtt = new double[MaxPlayers];
    // Guest: its own smoothed round trip to the host, kept apart from the host's table.
    private ushort _ownLatency;
    private uint _nextPing;
    internal event Action<int, double, double>? LatencySample;
    internal bool Paused { get; private set; }
    internal bool IsPresent(int slot) => _present[slot];
    internal PlayerProfile? Profile(int slot) => _profiles[slot];
    /// <summary>
    /// Round trip between the player in <paramref name="slot"/> and the host, in milliseconds. The host itself has
    /// none. A guest reports its own measurement for its slot and the host's measurements for the other guests.
    /// </summary>
    internal int? Ping(int slot)
    {
        if ((uint)slot >= MaxPlayers || slot == 0) return null;
        ushort latency = !IsHost && slot == LocalPlayerSlot ? _ownLatency : _latency[slot];
        return latency == 0 ? null : latency - 1;
    }
    internal bool IsReady(int slot) => (_readyMask & (1 << slot)) != 0;
    internal bool EveryoneReady => Enumerable.Range(0, MaxPlayers).All(slot => !_present[slot] || IsReady(slot));

    internal bool HeroTaken(int hero, int exceptSlot)
    {
        for (int slot = 0; slot < MaxPlayers; slot++)
            if (slot != exceptSlot && _present[slot] && _profiles[slot]?.Hero == hero) return true;
        return false;
    }

    internal static bool NicknameCharacter(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'А' and <= 'я' or 'Ё' or 'ё' or >= '0' and <= '9' or ' ' or '-' or '_';

    internal void SetProfile(int hero, string nickname)
    {
        if (!AcceptNewPlayers || hero < 0 || hero >= Math.Min(_characterCount, ushort.MaxValue)) return;
        var profile = new PlayerProfile(hero, nickname.Trim());
        if (profile.Nickname.Length > NicknameLimit || !profile.Nickname.All(NicknameCharacter)) return;
        if (LocalPlayerSlot is not int slot) _initialProfile = profile;
        else if (!IsReady(slot) && profile != _profiles[slot] && !HeroTaken(hero, slot))
        {
            if (IsHost) ApplyProfile(slot, profile);
            else SendProfile(profile);
        }
    }

    private static void WriteProfile(Span<byte> bytes, PlayerProfile? profile)
    {
        bytes.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(profile?.Hero ?? ushort.MaxValue));
        if (profile is not null) Encoding.UTF8.GetBytes(profile.Nickname, bytes[2..]);
    }

    private bool ReadProfile(ReadOnlySpan<byte> bytes, out PlayerProfile? profile)
    {
        profile = null;
        int hero = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        if (hero == ushort.MaxValue) return true;
        if (hero >= _characterCount) return false;
        bytes = bytes[2..];
        int end = bytes.IndexOf((byte)0);
        string nickname = Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
        if (nickname.Length > NicknameLimit || !nickname.All(NicknameCharacter)) return false;
        profile = new PlayerProfile(hero, nickname.Trim());
        return true;
    }

    private void SendProfile(PlayerProfile profile)
    {
        Span<byte> packet = stackalloc byte[HeaderBytes + ProfileBytes];
        Header(packet, Message.Profile);
        WriteProfile(packet[HeaderBytes..], profile);
        _transport.Send(_hostPeerId!, packet, reliable: true);
    }

    private void ReceiveProfile(int slot, ReadOnlySpan<byte> packet)
    {
        if (!AcceptNewPlayers || packet.Length != HeaderBytes + ProfileBytes ||
            !ReadProfile(packet[HeaderBytes..], out var profile) || profile is null) return;
        ApplyProfile(slot, profile);
    }

    private void ApplyProfile(int slot, PlayerProfile profile)
    {
        if (IsReady(slot) || profile == _profiles[slot]) return;
        if (HeroTaken(profile.Hero, slot))
        {
            // A joining player may have selected a hero already in use.
            if (_profiles[slot] is not null) return;
            int available = 0;
            while (available < Math.Min(_characterCount, ushort.MaxValue) && HeroTaken(available, slot))
                available++;
            if (available >= Math.Min(_characterCount, ushort.MaxValue)) return;
            profile = profile with { Hero = available };
        }
        _profiles[slot] = profile;
        BroadcastRoster();
    }

    internal void SetReady(bool ready)
    {
        if (!AcceptNewPlayers || LocalPlayerSlot is not int slot || _profiles[slot] is not { } profile) return;
        if (IsHost) ApplyReady(slot, ready, profile.Hero);
        else
        {
            Span<byte> packet = stackalloc byte[RunHeaderBytes + 3];
            WriteRunHeader(packet, Message.Ready);
            packet[RunHeaderBytes] = ready ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(packet[(RunHeaderBytes + 1)..], (ushort)profile.Hero);
            _transport.Send(_hostPeerId!, packet, reliable: true);
        }
    }

    private void ReceiveReady(int slot, ReadOnlySpan<byte> packet)
    {
        if (!AcceptNewPlayers || packet.Length != RunHeaderBytes + 3 || !MatchesRun(packet) || packet[RunHeaderBytes] > 1)
            return;
        ApplyReady(slot, packet[RunHeaderBytes] != 0, BinaryPrimitives.ReadUInt16LittleEndian(packet[(RunHeaderBytes + 1)..]));
    }

    private void ApplyReady(int slot, bool ready, int hero)
    {
        if (IsReady(slot) == ready || _profiles[slot]?.Hero != hero) return;
        if (ready) _readyMask |= (byte)(1 << slot);
        else _readyMask &= unchecked((byte)~(1 << slot));
        BroadcastRoster();
    }

    private void BroadcastRoster()
    {
        Span<byte> packet = stackalloc byte[HeaderBytes + 2 + MaxPlayers * ProfileBytes];
        Header(packet, Message.Roster);
        packet[HeaderBytes] = 0;
        packet[HeaderBytes + 1] = _readyMask;
        for (int slot = 0; slot < MaxPlayers; slot++)
        {
            if (_present[slot]) packet[HeaderBytes] |= (byte)(1 << slot);
            WriteProfile(packet.Slice(HeaderBytes + 2 + slot * ProfileBytes, ProfileBytes), _profiles[slot]);
        }
        BroadcastControl(packet);
    }

    private void ReceiveRoster(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != HeaderBytes + 2 + MaxPlayers * ProfileBytes) return;
        var profiles = new PlayerProfile?[MaxPlayers];
        for (int slot = 0; slot < MaxPlayers; slot++)
            if (!ReadProfile(packet.Slice(HeaderBytes + 2 + slot * ProfileBytes, ProfileBytes), out profiles[slot])) return;
        profiles.CopyTo(_profiles, 0);
        _readyMask = (byte)(packet[HeaderBytes + 1] & packet[HeaderBytes]);
        PlayerCount = 0;
        for (int slot = 0; slot < MaxPlayers; slot++)
        {
            _present[slot] = (packet[HeaderBytes] & (1 << slot)) != 0;
            if (_present[slot]) PlayerCount++;
        }
    }

    internal void SetPaused(bool paused)
    {
        if (!IsHost || Paused == paused) return;
        Paused = paused;
        Span<byte> packet = [ProtocolVersion, (byte)Message.Pause, paused ? (byte)1 : (byte)0];
        BroadcastControl(packet);
    }

    private void BroadcastControl(ReadOnlySpan<byte> packet)
    {
        for (int slot = 1; slot < MaxPlayers; slot++)
            if (_peerSlots[slot] is { } peer) _transport.Send(peer, packet, reliable: true);
    }

    private void SendPing(string peer, int slot, long now)
    {
        if (_lastPong[slot] != 0 && _clock.GetElapsedTime(_lastPong[slot], now).TotalSeconds >= 6)
        {
            _lastPong[slot] = 0;
            _smoothedRtt[slot] = 0;
            if (IsHost) _latency[slot] = 0;
            else _ownLatency = 0;
        }
        _pingSent[slot] = now;
        uint id = ++_nextPing;
        _pingId[slot] = id;
        Span<byte> packet = stackalloc byte[6];
        Header(packet, Message.Ping);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[2..], id);
        _transport.Send(peer, packet, reliable: false);
    }

    private void ReceivePing(string peer, int slot, Message message, ReadOnlySpan<byte> packet)
    {
        if (packet.Length != 6 || message is not (Message.Ping or Message.Pong)) return;
        long now = _clock.GetTimestamp();
        if (IsHost) _lastPeerActivity[slot] = now;
        else _lastHostActivity = now;
        if (message == Message.Ping)
        {
            Span<byte> reply = stackalloc byte[6];
            packet.CopyTo(reply);
            reply[1] = (byte)Message.Pong;
            _transport.Send(peer, reply, reliable: false);
            return;
        }
        if (_pingId[slot] == 0 || BinaryPrimitives.ReadUInt32LittleEndian(packet[2..]) != _pingId[slot]) return;
        _pingId[slot] = 0;
        _lastPong[slot] = now;
        double rtt = Math.Max(0, _clock.GetElapsedTime(_pingSent[slot], now).TotalMilliseconds);
        // A smoothed value keeps the display steady; one late pong moves it a quarter of the way.
        if (_smoothedRtt[slot] == 0) _smoothedRtt[slot] = Math.Max(rtt, 0.5);
        else
        {
            _jitter[slot] += (Math.Abs(rtt - _smoothedRtt[slot]) - _jitter[slot]) / 4;
            _smoothedRtt[slot] += (rtt - _smoothedRtt[slot]) / 4;
        }
        var latency = (ushort)(1 + Math.Clamp((int)Math.Round(_smoothedRtt[slot]), 0, ushort.MaxValue - 1));
        if (IsHost) _latency[slot] = latency;
        else _ownLatency = latency;
        LatencySample?.Invoke(IsHost ? slot : LocalPlayerSlot ?? 0, rtt, _jitter[slot]);
    }

    private void BroadcastLatency()
    {
        Span<byte> packet = stackalloc byte[HeaderBytes + 2 * MaxPlayers];
        Header(packet, Message.Latency);
        for (int slot = 0; slot < MaxPlayers; slot++)
            BinaryPrimitives.WriteUInt16LittleEndian(packet[(HeaderBytes + 2 * slot)..], _latency[slot]);
        for (int slot = 1; slot < MaxPlayers; slot++)
            if (_peerSlots[slot] is { } peer) _transport.Send(peer, packet, reliable: false);
    }

    private void ReceiveLatency(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != HeaderBytes + 2 * MaxPlayers) return;
        // Slot 0 is the host and carries no ping; the guest's own slot keeps its local measurement.
        for (int slot = 1; slot < MaxPlayers; slot++)
            _latency[slot] = BinaryPrimitives.ReadUInt16LittleEndian(packet[(HeaderBytes + 2 * slot)..]);
    }
}
