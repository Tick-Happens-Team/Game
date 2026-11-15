using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using TickHappens.Localization;
using System.Security.Cryptography;
using System.Text.Json;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game.Online;

internal sealed class OnlineGame(ContentSet content, int lanPort = LanTransport.DefaultPort, bool collectStatistics = false) : IDisposable
{
    private const int SnapshotTicks = 3;
    private const int KeyframeTicks = RunWorld.TicksPerSecond;
    private const int InputHistoryLength = 128;
    private const float ResultsSeconds = 3f;
    private sealed record ReconnectTarget(IPEndPoint? LanAddress, string? EosCode, string Character, uint Seed, Guid Key);
    private ReconnectTarget? _reconnect;
    private bool _resuming;
    private readonly byte[] _eventPacket = new byte[CoopSession.MaxEventBytes];
    private readonly byte[] _snapshot = new byte[RunWorld.SnapshotCapacity];
    private readonly byte[] _encodedSnapshot = new byte[RunWorld.SnapshotCapacity + SnapshotDelta.MaxHeaderBytes];
    private readonly byte[] _baseline = new byte[RunWorld.SnapshotCapacity];
    private int _baselineTick = -1, _baselineBytes;
    private readonly RunInput[] _inputHistory = new RunInput[InputHistoryLength];
    private int _lastSubmittedInput;
    private readonly RunInput[] _inputs = new RunInput[4];
    private readonly int[] _lastInput = new int[4];
    private readonly int[] _receivedInputSequence = new int[4];
    private LanTransport? _lan;
#if TICKHAPPENS_EOS
    private EosTransport? _transport;
#endif
    private CoopSession? _session;
    private Task? _operation;
#if TICKHAPPENS_EOS
    private string? _joinCode;
#endif
    private string? _character;
    private uint _seed;
    private double _accumulator;
    private float _resultsTime;
#if TICKHAPPENS_EOS
    private bool _signingIn;
#endif
    private bool _leaving;
    private bool _requestLeave;
    private bool _hostEnded;
    private bool _started;
    private bool _sendResumeSnapshot;
    private long? _lastSnapshotAt;
    private int _lastMeasuredSnapshotTick;
    private double _snapshotInterval, _snapshotJitter;
    private float _correction;
    private bool _relay;
    private Vector2 _viewCorrection;

    public RunWorld? World { get; private set; }
    public string? RoomCode { get; private set; }
    public string Status { get; private set; } = "";
    public bool Busy => _operation is not null;
    public bool InRoom => _session is not null;
    public bool IsHost => _session?.IsHost == true;
    public int? LanPort => IsHost ? _lan?.Port : null;
    internal long SentLanBytes => _lan?.SentBytes ?? 0;
    internal int LastSubmittedInput => _lastSubmittedInput;
    public int PlayerCount => _session?.PlayerCount ?? 0;
    public bool CanStart => IsHost && !_started && PlayerCount >= 2 && _session!.ProfilesReady;
    public bool Paused => _session?.Paused == true;
    public int? LocalSlot => _session?.LocalPlayerSlot;
    public int HeroCount => content.Characters.Count;
    public string Nickname { get; private set; } = "";
    public int SelectedHero { get; private set; }
    public CharacterDef Hero(int index) => content.Characters[index];
    public string HeroName(int hero) => Hud.CharacterName(Hero(hero));
    public bool PlayerPresent(int slot) => _session?.IsPresent(slot) == true;
    public int? PlayerHero(int slot) => _session?.Profile(slot)?.Hero;
    public string PlayerName(int slot) => _session?.Profile(slot)?.Nickname is { Length: > 0 } name
        ? name : Localizer.Format("online.player", slot + 1);
    public int? Ping(int slot) => _session?.Ping(slot);
    public int? LocalPing
    {
        get
        {
            if (!IsHost) return Ping(0);
            int? highest = null;
            for (int slot = 1; slot < CoopSession.MaxPlayers; slot++)
                if (Ping(slot) is int ping && ping > (highest ?? -1)) highest = ping;
            return highest;
        }
    }
    public Vector2 ViewCorrection => _viewCorrection;

    public void SetPaused(bool paused) => _session?.SetPaused(paused);

    public void EditProfile(int hero, string nickname)
    {
        if (_started || LocalSlot is null || (uint)hero >= (uint)HeroCount) return;
        SelectedHero = hero;
        Nickname = nickname;
        _session!.SetProfile(hero, nickname);
    }
    public bool Interrupted { get; private set; }
    public bool CanReconnect => Interrupted && !Busy && !InRoom && _reconnect is not null;
    public bool Playing => _started && World is not null;
    public float Alpha => Interrupted || World?.Phase == RunPhase.Dead
        ? 1
        : Math.Clamp((float)(_accumulator / (RunWorld.Dt * (IsHost ? 1 : SnapshotTicks))), 0, 1);
    public float LocalAlpha => IsHost || Interrupted || World?.Phase == RunPhase.Dead
        ? Alpha
        : Math.Clamp((float)(_inputTime / RunWorld.Dt), 0, 1);

    public void Reconnect()
    {
        if (!CanReconnect || _reconnect is not { } target) return;
        _resuming = true;
        if (target.LanAddress is { } address)
            ConnectLan(false, address, target.Character, target.Seed);
#if TICKHAPPENS_EOS
        else
            Connect(target.EosCode, target.Character, target.Seed);
#endif
    }

    public void ConnectLan(bool host, IPEndPoint? address, string character, uint seed)
    {
        if (Busy || InRoom) return;
        try
        {
            _character = character;
            _seed = seed;
            _lan = new LanTransport(host, lanPort, collectStatistics);
            if (host) OpenSession(_lan, null);
            else
            {
                Status = Localizer.Get("online.searching");
                _operation = address is null ? _lan.DiscoverAsync() : _lan.JoinAsync(address);
            }
        }
        catch (Exception error)
        {
            CloseRoom(Interrupted);
            SetError(error);
        }
    }

#if TICKHAPPENS_EOS
    private long? _p2pWaitStarted;
    private bool _forceRelays = LocalEosConfig.ForceRelays;

    public bool ForceRelays
    {
        get => _forceRelays;
        set
        {
            if (Busy || InRoom || value == _forceRelays) return;
            try
            {
                _transport?.SetRelayMode(value);
                _forceRelays = value;
            }
            catch (InvalidOperationException error) { SetError(error); }
        }
    }

    public void Connect(string? code, string character, uint seed)
    {
        if (Busy || InRoom) return;
        try
        {
            _joinCode = code;
            _character = character;
            _seed = seed;
            if (_transport is null)
            {
                _transport = new EosTransport(LocalEosConfig.Load(), ForceRelays);
                _transport.PeerConnected += (_, relayed) =>
                {
                    _relay |= relayed;
                    EosTransport.LogConnection(relayed ? "EOS P2P relay established." : "EOS P2P direct connection established.");
                };
            }
            _signingIn = _transport.LocalPeer is null;
            if (_signingIn)
            {
                Status = Localizer.Get("online.signIn");
                _operation = _transport.SignInAsync();
            }
            else BeginRoom();
        }
        catch (Exception error)
        {
            CloseRoom(Interrupted);
            SetError(error);
        }
    }

    private void BeginRoom()
    {
        Status = _joinCode is null ? Localizer.Get("online.creating") : Localizer.Get("online.searching");
        _operation = _joinCode is null ? _transport!.HostAsync(4) : _transport!.JoinAsync(_joinCode);
    }
#endif

    private void CompleteOperation()
    {
        Task operation = _operation!;
        _operation = null;
        try
        {
            operation.GetAwaiter().GetResult();
            if (_leaving)
            {
                _leaving = false;
                return;
            }
            if (_requestLeave)
            {
                _requestLeave = false;
                Leave();
                return;
            }
            if (_lan is not null)
            {
                OpenSession(_lan, _lan.HostPeer ?? throw new InvalidOperationException("LAN connection has no host."));
                return;
            }
#if TICKHAPPENS_EOS
            if (_signingIn)
            {
                _signingIn = false;
                BeginRoom();
                return;
            }
            bool host = _joinCode is null;
            RoomCode = host ? ((Task<string>)operation).Result : null;
            OpenSession(_transport!, host ? null :
                _transport!.HostPeer ?? throw new InvalidOperationException("EOS lobby has no host."));
#endif
        }
        catch (Exception error)
        {
            bool wasLeaving = _leaving;
            _leaving = _requestLeave = false;
#if TICKHAPPENS_EOS
            _signingIn = false;
#endif
            if (!wasLeaving) CloseRoom(Interrupted);
            SetError(error);
        }
    }

    private void OpenSession(ICoopTransport transport, string? hostPeer)
    {
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content, ContentSet.Json)));
        Guid key = _resuming && _reconnect is { } target ? target.Key : Guid.NewGuid();
        var session = new CoopSession(transport, hash, hostPeer, resumeKey: key, resume: _resuming,
            characterCount: content.Characters.Count);
        _session = session;
        if (!_resuming) SelectedHero = content.CharacterIndex(_character!);
        session.SetProfile(SelectedHero, Nickname);
        session.LatencySample += LogLatency;
        if (session.IsHost)
        {
            session.PeerJoined += slot => World?.SetPlayerActive(slot, true);
            session.PeerResumed += slot =>
            {
                if (World is null) return;
                World.RestoreCoopPlayer(slot);
                _sendResumeSnapshot = true;
            };
            session.PeerLeft += slot =>
            {
                _inputs[slot] = default;
                _receivedInputSequence[slot] = 0;
                World?.SetPlayerActive(slot, false);
            };
            session.InputReceived += (slot, sequence, input) =>
            {
                if (World is null || World.Phase == RunPhase.Dead) return;
                _inputs[slot] = input with
                {
                    Dash = _inputs[slot].Dash || input.Dash,
                    ChestChoice = input.ChestChoice != 0 ? input.ChestChoice : _inputs[slot].ChestChoice,
                };
                _lastInput[slot] = World.Tick;
                _receivedInputSequence[slot] = sequence;
            };
            Status = Localizer.Get("online.waitingPlayers");
        }
        else
        {
            session.HostDisconnected += () => _hostEnded = true;
            string? eosCode = null;
#if TICKHAPPENS_EOS
            if (_lan is null) eosCode = _joinCode;
#endif
            _reconnect = new ReconnectTarget(_lan?.HostAddress, eosCode, _character!, _seed, key);
            RunWorld? replica = null;
            session.RunStateChanged += () =>
            {
                replica = null;
                ResetRun(keepResults: false);
                _resuming = false;
            };
            RunWorld Replica(int slot) =>
                replica ??= new RunWorld(content, _seed, _character!, playerCount: 4, localPlayerId: slot);
            session.EventsReceived += packet =>
            {
                if (session.LocalPlayerSlot is int slot) Replica(slot).TryApplyEvents(packet.Span);
            };
            void Apply(ReadOnlySpan<byte> snapshot)
            {
                if (session.LocalPlayerSlot is not int slot) return;
                var world = Replica(slot);
                bool running = Playing && !Interrupted;
                Vector2 previous = Vector2.Lerp(world.Player.Prev, world.Player.Pos, LocalAlpha) + _viewCorrection;
                if (!world.TryApplySnapshot(snapshot, Alpha)) return;
                World = world;
                _resuming = Interrupted = false;
                _started = true;
                _accumulator = 0;

                int firstPending = Math.Max(world.Player.LastAppliedInput + 1, _lastSubmittedInput - InputHistoryLength + 1);
                for (int sequence = firstPending; sequence <= _lastSubmittedInput; sequence++)
                    world.PredictLocalMovement(_inputHistory[sequence % InputHistoryLength]);
                Vector2 corrected = Vector2.Lerp(world.Player.Prev, world.Player.Pos, LocalAlpha);
                Vector2 offset = previous - corrected;
                _viewCorrection = running && offset.LengthSquared() < 48 * 48 ? offset : Vector2.Zero;
                if (running) _correction = Math.Max(_correction, offset.Length());
                MeasureSnapshot(world.Tick);
            }

            session.KeyframeReceived += (tick, packet) =>
            {
                if (packet.Length > _baseline.Length) return;
                packet.Span.CopyTo(_baseline);
                _baselineTick = tick;
                _baselineBytes = packet.Length;
                Apply(packet.Span);
            };
            session.SnapshotReceived += (_, packet) =>
            {
                int size = SnapshotDelta.Decode(_baselineTick, _baseline.AsSpan(0, _baselineBytes), packet.Span, _snapshot);
                if (size > 0) Apply(_snapshot.AsSpan(0, size));
            };
#if TICKHAPPENS_EOS
            if (_lan is null && _transport is { IsHostConnected: false })
            {
                _p2pWaitStarted = Stopwatch.GetTimestamp();
                Status = Localizer.Get("online.waitingP2p");
                return;
            }
#endif
            session.Join();
            Status = Localizer.Get("online.waitingHost");
        }
    }

#if TICKHAPPENS_EOS
    private void UpdateP2pJoin()
    {
        if (_p2pWaitStarted is not long started || _session is not { JoinError: null } session) return;

        var elapsed = Stopwatch.GetElapsedTime(started);
        if (_transport!.IsHostConnected)
        {
            _p2pWaitStarted = null;
            EosTransport.LogConnection($"EOS P2P ready after {elapsed.TotalSeconds:F1}s; requesting game slot.");
            session.Join();
        }
        else if (elapsed >= TimeSpan.FromSeconds(45))
        {
            EosTransport.LogConnection($"EOS P2P channel timed out after {elapsed.TotalSeconds:F1}s.");
            CloseRoom(Interrupted);
            Status = Localizer.Get("online.p2pTimeout");
        }
    }
#endif

    public void Start()
    {
        if (!CanStart || _session is not { } session) return;
        World = new RunWorld(content, _seed, content.Characters[SelectedHero].Id,
            playerCount: CoopSession.MaxPlayers, localPlayerId: 0);
        _seed = (uint)Random.Shared.NextInt64(1L << 32);
        for (int slot = 0; slot < CoopSession.MaxPlayers; slot++)
        {
            if (!session.IsPresent(slot) || PlayerHero(slot) is not int hero) continue;
            World.SetPlayerActive(slot, true);
            World.SetStartingHero(slot, hero);
        }
        session.SetRunActive(true);
        if (_lan is not null) _lan.Discoverable = false;
        _started = true;
        SendSnapshot(session, World, forceKeyframe: true);
    }

    private void ReturnToLobby(CoopSession session)
    {
        ResetRun(keepResults: false);
        session.SetRunActive(false);
        if (_lan is not null) _lan.Discoverable = true;
        Status = Localizer.Get("online.waitingPlayers");
    }

    public void Update(float dt, RunInput input)
    {
        try
        {
            if (!Paused) _viewCorrection *= MathF.Exp(-dt * 18);
            _lan?.Pump();
#if TICKHAPPENS_EOS
            if (_lan is null) _transport?.Pump();
#endif
            if (_operation?.IsCompleted == true) CompleteOperation();
#if TICKHAPPENS_EOS
            UpdateP2pJoin();
#endif
            _session?.Poll();
            if (_session is { IsHost: false } guest && !_started)
            {
                string key = guest.LocalPlayerSlot.HasValue ? "online.waitingStart" : "online.waitingHost";
#if TICKHAPPENS_EOS
                if (_p2pWaitStarted.HasValue)
                    key = "online.waitingP2p";
#endif
                Status = Localizer.Get(key);
            }
            if (_hostEnded || _session?.JoinError is not null)
            {
                string key = _session?.JoinError ?? "online.hostClosed";
#if TICKHAPPENS_EOS
                if (_lan is null && _transport is not null)
                {
                    EosTransport.LogConnection($"EOS session ended: {key}.");
                }
#endif
                string message = Localizer.Get(key);
                bool keepResults = Interrupted || (_hostEnded && Playing);
                CloseRoom(keepResults);
                Status = message;
            }
            if (_session is null || !Playing || Interrupted) return;
            if (World!.Phase == RunPhase.Dead)
            {
                if (IsHost)
                {
                    if (_sendResumeSnapshot)
                    {
                        SendSnapshot(_session, World, forceKeyframe: true);
                        _sendResumeSnapshot = false;
                    }
                    _resultsTime += dt;
                    if (_resultsTime >= ResultsSeconds) ReturnToLobby(_session);
                }
                return;
            }
            if (Paused) return;
            _accumulator += dt;
            if (IsHost)
            {
                _inputs[0] = input with
                {
                    Dash = _inputs[0].Dash || input.Dash,
                    ChestChoice = input.ChestChoice != 0 ? input.ChestChoice : _inputs[0].ChestChoice,
                };
                int nextEvent = World!.Events.Count;
                while (_accumulator + 1e-8 >= RunWorld.Dt)
                {
                    for (int slot = 1; slot < _inputs.Length; slot++)
                    {
                        if (World.Tick - _lastInput[slot] > RunWorld.TicksPerSecond / 3) _inputs[slot] = default;
                        World.Players[slot].LastAppliedInput = _receivedInputSequence[slot];
                    }
                    World.StepCoop(_inputs);
                    for (int slot = 0; slot < _inputs.Length; slot++) _inputs[slot] = _inputs[slot] with { Dash = false, ChestChoice = 0 };
                    bool ended = World.Phase == RunPhase.Dead;
                    if (ended || _sendResumeSnapshot || World.Tick % SnapshotTicks == 0)
                    {
                        SendSnapshot(_session, World, forceKeyframe: ended || _sendResumeSnapshot);
                        _sendResumeSnapshot = false;
                    }
                    while (nextEvent < World.Events.Count)
                    {
                        int bytes = World.WriteEvents(_eventPacket, ref nextEvent);
                        if (bytes == 0) break;
                        _session.BroadcastEvents(_eventPacket.AsSpan(0, bytes));
                    }
                    _accumulator = Math.Max(0, _accumulator - RunWorld.Dt);
                    if (ended) break;
                }
            }
            else
            {
                _inputTime += dt;
                _dashQueued |= input.Dash;
                if (input.ChestChoice != 0) _chestChoiceQueued = input.ChestChoice;
                while (_inputTime + 1e-8 >= RunWorld.Dt)
                {
                    _inputTime = Math.Max(0, _inputTime - RunWorld.Dt);
                    var sample = input with { Dash = _dashQueued, ChestChoice = _chestChoiceQueued };
                    int sequence = _session.SubmitInput(sample);
                    if (sequence > 0)
                    {
                        _lastSubmittedInput = sequence;
                        _inputHistory[sequence % InputHistoryLength] = sample;
                        World!.PredictLocalMovement(sample);
                    }
                    _dashQueued = false;
                    _chestChoiceQueued = 0;
                }
            }
        }
        catch (Exception error)
        {
            CloseRoom(Interrupted);
            SetError(error);
        }
    }

    private void MeasureSnapshot(int tick)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastSnapshotAt is long previous && tick > _lastMeasuredSnapshotTick)
        {
            _snapshotInterval = Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;
            double expected = (tick - _lastMeasuredSnapshotTick) * 1000d / RunWorld.TicksPerSecond;
            _snapshotJitter += (Math.Abs(_snapshotInterval - expected) - _snapshotJitter) / 4;
        }
        _lastMeasuredSnapshotTick = tick;
        _lastSnapshotAt = now;
    }

    private void LogLatency(int slot, double rtt, double jitter)
    {
        string transport = _lan is not null ? "LAN" : _relay ? "EOS relay" : "EOS";
        WriteNetworkLog(string.Create(CultureInfo.InvariantCulture,
            $"{transport} slot={slot} rtt_ms={rtt:F1} jitter_ms={jitter:F1} snapshot_ms={_snapshotInterval:F1} snapshot_jitter_ms={_snapshotJitter:F1} correction_px={_correction:F1}"));
        _correction = 0;
    }

    internal static void WriteNetworkLog(string message)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TickHappens", "coop-network.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (new FileInfo(path) is { Exists: true, Length: >= 256 * 1024 })
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void SendSnapshot(CoopSession session, RunWorld world, bool forceKeyframe)
    {
        int size = world.WriteSnapshot(_snapshot);
        if (size == 0) return;
        var snapshot = _snapshot.AsSpan(0, size);
        if (forceKeyframe || _baselineTick < 0 || world.Tick - _baselineTick >= KeyframeTicks)
        {
            session.BroadcastKeyframe(world.Tick, snapshot);
            snapshot.CopyTo(_baseline);
            _baselineTick = world.Tick;
            _baselineBytes = size;
            return;
        }

        int bytes = SnapshotDelta.Encode(_baselineTick, _baseline.AsSpan(0, _baselineBytes), snapshot, _encodedSnapshot);
        session.BroadcastSnapshot(world.Tick, _encodedSnapshot.AsSpan(0, bytes));
    }

    private double _inputTime;
    private bool _dashQueued;
    private int _chestChoiceQueued;

    public void Leave() => CloseRoom(keepResults: false);

    private void CloseRoom(bool keepResults)
    {
        if (Busy && _lan is null)
        {
            if (!_leaving) _requestLeave = true;
            return;
        }
        _session?.Dispose();
        _session = null;
        ResetRun(keepResults);
        if (!keepResults) _reconnect = null;
        _relay = false;
        _hostEnded = _resuming = false;
#if TICKHAPPENS_EOS
        _p2pWaitStarted = null;
#endif
        RoomCode = null;
        Status = Localizer.Get("online.closed");
        if (_lan is not null)
        {
            _lan.Dispose();
            _lan = null;
            _operation = null;
            return;
        }
#if TICKHAPPENS_EOS
        if (_transport is null) return;
        _leaving = true;
        try { _operation = _transport.LeaveAsync(); }
        catch (Exception error) { _leaving = false; SetError(error); }
#endif
    }

    private void ResetRun(bool keepResults)
    {
        if (!keepResults) World = null;
        _started = Interrupted = keepResults;
        _viewCorrection = Vector2.Zero;
        _lastSnapshotAt = null;
        _snapshotInterval = _snapshotJitter = _correction = 0;
        _dashQueued = _sendResumeSnapshot = false;
        _accumulator = _inputTime = _resultsTime = 0;
        _lastSubmittedInput = _baselineBytes = 0;
        _baselineTick = -1;
        _chestChoiceQueued = 0;
        Array.Clear(_inputs);
        Array.Clear(_lastInput);
        Array.Clear(_receivedInputSequence);
    }

    private void SetError(Exception error)
    {
        // Configuration, platform and transport exceptions do not need to expose file contents.
        Console.Error.WriteLine(error is InvalidOperationException ? error.Message : error.GetType().Name);
        Status = Localizer.Get(error is LocalizedException failure ? failure.Key : "online.error");
    }

    public void Dispose()
    {
        Leave();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (_operation is not null && timeout.ElapsedMilliseconds < 1500)
        {
            Update(0, default);
            Thread.Sleep(5);
        }
#if TICKHAPPENS_EOS
        _transport?.Dispose();
#endif
    }
}
