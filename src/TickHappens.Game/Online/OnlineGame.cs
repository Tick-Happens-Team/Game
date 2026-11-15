using System.Net;
using TickHappens.Localization;
using System.Security.Cryptography;
using System.Text.Json;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game.Online;

internal sealed class OnlineGame(ContentSet content) : IDisposable
{
    private const int SnapshotTicks = 3;
    private readonly byte[] _eventPacket = new byte[CoopSession.MaxEventBytes];
    private readonly byte[] _snapshot = new byte[RunWorld.SnapshotCapacity];
    private readonly RunInput[] _inputs = new RunInput[4];
    private readonly int[] _lastInput = new int[4];
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
#if TICKHAPPENS_EOS
    private bool _signingIn;
#endif
    private bool _leaving;
    private bool _requestLeave;
    private bool _hostEnded;
    private bool _started;
    private bool _sentFinal;

    public RunWorld? World { get; private set; }
    public string? RoomCode { get; private set; }
    public string Status { get; private set; } = "";
    public bool Busy => _operation is not null;
    public bool InRoom => _session is not null;
    public bool IsHost => _session?.IsHost == true;
    public int? LanPort => IsHost ? _lan?.Port : null;
    public int PlayerCount => _session?.PlayerCount ?? 0;
    public bool Interrupted { get; private set; }
    public bool Playing => _started && World is not null;
    public float Alpha => Math.Clamp((float)(_accumulator / (RunWorld.Dt * (IsHost ? 1 : SnapshotTicks))), 0, 1);

    public void ConnectLan(bool host, IPEndPoint? address, string character, uint seed)
    {
        if (Busy || InRoom) return;
        try
        {
            _character = character;
            _seed = seed;
            _lan = new LanTransport(host);
            if (host) OpenSession(_lan, null);
            else
            {
                Status = Localizer.Get("online.searching");
                _operation = address is null ? _lan.DiscoverAsync() : _lan.JoinAsync(address);
            }
        }
        catch (Exception error)
        {
            Leave();
            SetError(error);
        }
    }

#if TICKHAPPENS_EOS
    private bool _forceRelays = LocalEosConfig.ForceRelays;

    public bool ForceRelays
    {
        get => _forceRelays;
        set
        {
            if (Busy || InRoom || value == _forceRelays) return;
            _transport?.Dispose();
            _transport = null;
            _forceRelays = value;
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
                    Console.WriteLine(relayed ? "EOS P2P relay established." : "EOS P2P direct connection established.");
            }
            _signingIn = _transport.LocalPeer is null;
            if (_signingIn)
            {
                Status = Localizer.Get("online.signIn");
                _operation = _transport.SignInAsync();
            }
            else BeginRoom();
        }
        catch (Exception error) { SetError(error); }
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
            if (!wasLeaving) Leave();
            SetError(error);
        }
    }

    private void OpenSession(ICoopTransport transport, string? hostPeer)
    {
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content, ContentSet.Json)));
        var session = new CoopSession(transport, hash, hostPeer);
        _session = session;
        if (session.IsHost)
        {
            World = new RunWorld(content, _seed, _character!, playerCount: 4, localPlayerId: 0);
            session.PeerJoined += slot => World.SetPlayerActive(slot, true);
            session.PeerLeft += slot =>
            {
                _inputs[slot] = default;
                World.SetPlayerActive(slot, false);
            };
            session.InputReceived += (slot, _, input) =>
            {
                _inputs[slot] = input with { Dash = _inputs[slot].Dash || input.Dash };
                _lastInput[slot] = World.Tick;
            };
            Status = Localizer.Get("online.waitingPlayers");
        }
        else
        {
            session.HostDisconnected += () => _hostEnded = true;
            RunWorld Replica(int slot) =>
                World ??= new RunWorld(content, _seed, _character!, playerCount: 4, localPlayerId: slot);
            session.EventsReceived += packet =>
            {
                if (session.LocalPlayerSlot is int slot) Replica(slot).TryApplyEvents(packet.Span);
            };
            session.SnapshotReceived += (_, packet) =>
            {
                if (session.LocalPlayerSlot is not int slot) return;
                var world = Replica(slot);
                if (!world.TryApplySnapshot(packet.Span)) return;
                _started = true;
                _accumulator = 0;
            };
            session.Join();
            Status = Localizer.Get("online.waitingHost");
        }
    }

    public void Start()
    {
        if (!IsHost || PlayerCount < 2 || _started) return;
        _session!.AcceptNewPlayers = false;
        if (_lan is not null) _lan.Discoverable = false;
        _started = true;
        _accumulator = 0;
    }

    public void Update(float dt, RunInput input)
    {
        try
        {
            _lan?.Pump();
#if TICKHAPPENS_EOS
            if (_lan is null) _transport?.Pump();
#endif
            if (_operation?.IsCompleted == true) CompleteOperation();
            _session?.Poll();
            if (_hostEnded || _session?.JoinError is not null)
            {
                string message = Localizer.Get(_session?.JoinError ?? "online.hostClosed");
                var results = _hostEnded && Playing ? World : null;
                _hostEnded = false;
                Leave();
                if (results is not null)
                {
                    World = results;
                    _started = Interrupted = true;
                }
                Status = message;
            }
            if (_session is null) return;
            if (!Playing) return;
            _accumulator += dt;
            if (IsHost)
            {
                _inputs[0] = input with { Dash = _inputs[0].Dash || input.Dash };
                int nextEvent = World!.Events.Count;
                while (_accumulator + 1e-8 >= RunWorld.Dt)
                {
                    for (int slot = 1; slot < _inputs.Length; slot++)
                        if (World!.Tick - _lastInput[slot] > RunWorld.TicksPerSecond / 3) _inputs[slot] = default;
                    World!.StepCoop(_inputs);
                    for (int slot = 0; slot < _inputs.Length; slot++) _inputs[slot] = _inputs[slot] with { Dash = false };
                    bool ended = World.Phase == RunPhase.Dead;
                    if ((!ended && World.Tick % SnapshotTicks == 0) || (ended && !_sentFinal))
                    {
                        int size = World.WriteSnapshot(_snapshot);
                        if (size > 0) _session.BroadcastSnapshot(World.Tick, _snapshot.AsSpan(0, size), reliable: ended);
                        _sentFinal = ended;
                    }
                    while (nextEvent < World.Events.Count)
                    {
                        int bytes = World.WriteEvents(_eventPacket, ref nextEvent);
                        if (bytes == 0) break;
                        _session.BroadcastEvents(_eventPacket.AsSpan(0, bytes));
                    }
                    _accumulator = Math.Max(0, _accumulator - RunWorld.Dt);
                }
            }
            else
            {
                _inputTime += dt;
                _dashQueued |= input.Dash;
                while (_inputTime + 1e-8 >= RunWorld.Dt)
                {
                    _inputTime = Math.Max(0, _inputTime - RunWorld.Dt);
                    _session.SubmitInput(input with { Dash = _dashQueued });
                    _dashQueued = false;
                }
            }
        }
        catch (Exception error)
        {
            Leave();
            SetError(error);
        }
    }

    private double _inputTime;
    private bool _dashQueued;

    public void Leave()
    {
        if (Busy && _lan is null)
        {
            if (!_leaving) _requestLeave = true;
            return;
        }
        _session?.Dispose();
        _session = null;
        World = null;
        RoomCode = null;
        _started = _dashQueued = _sentFinal = Interrupted = _hostEnded = false;
        _accumulator = _inputTime = 0;
        Array.Clear(_inputs);
        Array.Clear(_lastInput);
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
