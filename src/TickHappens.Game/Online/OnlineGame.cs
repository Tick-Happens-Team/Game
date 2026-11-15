#if TICKHAPPENS_EOS
using TickHappens.Localization;
using System.Security.Cryptography;
using System.Text.Json;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game.Online;

internal sealed class OnlineGame(ContentSet content) : IDisposable
{
    private const int SnapshotTicks = 3;
    private readonly byte[] _snapshot = new byte[RunWorld.SnapshotCapacity];
    private readonly RunInput[] _inputs = new RunInput[4];
    private readonly int[] _lastInput = new int[4];
    private EosTransport? _transport;
    private CoopSession? _session;
    private Task? _operation;
    private string? _joinCode;
    private string? _character;
    private uint _seed;
    private double _accumulator;
    private bool _signingIn;
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
    public bool IsHost => RoomCode is not null;
    public int PlayerCount => _session?.PlayerCount ?? 0;
    public bool Playing => _started && World is not null;
    public float Alpha => Math.Clamp((float)(_accumulator / (RunWorld.Dt * (IsHost ? 1 : SnapshotTicks))), 0, 1);

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
                _transport = new EosTransport(LocalEosConfig.Load(), LocalEosConfig.ForceRelays);
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
            if (_signingIn)
            {
                _signingIn = false;
                BeginRoom();
                return;
            }
            bool host = _joinCode is null;
            RoomCode = host ? ((Task<string>)operation).Result : null;
            string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content, ContentSet.Json)));
            var session = new CoopSession(_transport!, hash, host ? null :
                _transport!.HostPeer ?? throw new InvalidOperationException("EOS lobby has no host."));
            _session = session;
            if (host)
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
                session.SnapshotReceived += (_, packet) =>
                {
                    if (session.LocalPlayerSlot is not int slot) return;
                    var world = World ?? new RunWorld(content, _seed, _character!, playerCount: 4, localPlayerId: slot);
                    if (!world.TryApplySnapshot(packet.Span)) return;
                    World = world;
                    _started = true;
                    _accumulator = 0;
                };
                session.Join();
                Status = Localizer.Get("online.waitingHost");
            }
        }
        catch (Exception error)
        {
            bool wasLeaving = _leaving;
            _signingIn = _leaving = _requestLeave = false;
            if (!wasLeaving && _session is not null) Leave();
            SetError(error);
        }
    }

    public void Start()
    {
        if (!IsHost || PlayerCount < 2 || _started) return;
        _session!.AcceptNewPlayers = false;
        _started = true;
        _accumulator = 0;
    }

    public void Update(float dt, RunInput input)
    {
        try
        {
            _transport?.Pump();
            if (_operation?.IsCompleted == true) CompleteOperation();
            _session?.Poll();
            if (_hostEnded || _session?.JoinError is not null)
            {
                string message = Localizer.Get(_session?.JoinError ?? "online.hostClosed");
                _hostEnded = false;
                Leave();
                Status = message;
            }
            if (_session is null) return;
            if (!Playing) return;
            _accumulator += dt;
            if (IsHost)
            {
                _inputs[0] = input with { Dash = _inputs[0].Dash || input.Dash };
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
                    _accumulator = Math.Max(0, _accumulator - RunWorld.Dt);
                }
            }
            else
            {
                _inputTime += dt;
                _dashQueued |= input.Dash;
                if (_inputTime >= RunWorld.Dt)
                {
                    _inputTime -= RunWorld.Dt;
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
        if (Busy)
        {
            if (!_leaving) _requestLeave = true;
            return;
        }
        _session?.Dispose();
        _session = null;
        World = null;
        RoomCode = null;
        _started = _dashQueued = _sentFinal = false;
        _accumulator = _inputTime = 0;
        Array.Clear(_inputs);
        Array.Clear(_lastInput);
        Status = Localizer.Get("online.closed");
        if (_transport is null) return;
        _leaving = true;
        try { _operation = _transport.LeaveAsync(); }
        catch (Exception error) { _leaving = false; SetError(error); }
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
        _transport?.Dispose();
    }
}
#endif
