#if TICKHAPPENS_EOS
using System.Diagnostics;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;

namespace TickHappens.Game.Online;

public sealed class EosConfig(string productId, string sandboxId, string deploymentId, string clientId,
    string clientSecret, string? cacheDirectory = null)
{
    public string ProductId { get; } = productId;
    public string SandboxId { get; } = sandboxId;
    public string DeploymentId { get; } = deploymentId;
    public string ClientId { get; } = clientId;
    public string ClientSecret { get; } = clientSecret;
    public string? CacheDirectory { get; } = cacheDirectory;
}

/// <summary>Single-threaded EOS transport. Call its methods and pump from the platform thread.</summary>
public sealed partial class EosTransport : ICoopTransport, IDisposable
{
    private const string Bucket = "tickhappens-v1";
    private const string CodeAttribute = "TH_ROOM";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string SocketName = "TickHappens";
    private const byte Channel = 21;
    private readonly PlatformInterface _platform;
    private readonly ConnectInterface _connect;
    private readonly LobbyInterface _lobby;
    private readonly P2PInterface _p2p;
    private readonly SocketId _socket = new() { SocketName = SocketName };
    private readonly Dictionary<string, ProductUserId> _members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProductUserId> _refreshed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _connected = new(StringComparer.Ordinal);
    private readonly List<string> _removed = [];
    private readonly Queue<Action> _completed = new();
    private readonly byte[] _incoming = new byte[P2PInterface.MAX_PACKET_SIZE];
    private readonly byte[] _outgoing = new byte[P2PInterface.MAX_PACKET_SIZE];
    private ReceivePacketOptions _receiveOptions;
    private ProductUserId _receivedUser = new();
    private SocketId _receivedSocket;
    private ProductUserId? _local;
    private string? _lobbyId;
    private ulong _memberNotification, _requestNotification, _closedNotification, _establishedNotification, _expirationNotification;
    private long _nextMembershipRefresh;
    private Result? _lastReceiveError, _lastSendError, _lastAcceptError;
    private bool _refreshMembers, _busy, _disposed;
    private bool _receivedRoomPacket;
    private bool _sentRoomPacket;

    public string? LocalPeer { get; private set; }
    public string? HostPeer { get; private set; }
    public bool IsHost => LocalPeer is not null && LocalPeer == HostPeer;
    public bool IsHostConnected => HostPeer is { } host && _connected.Contains(host);
    public IReadOnlyCollection<string> Peers => _members.Keys;
    /// <summary>The packet memory is valid only while the handler runs.</summary>
    public event Action<string, ReadOnlyMemory<byte>>? PacketReceived;
    public event Action<string>? PeerDisconnected;
    public event Action<string, bool>? PeerConnected;

    public EosTransport(EosConfig config, bool forceRelays = false)
    {
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            throw new PlatformNotSupportedException("This EOS build requires Windows x64.");

        var init = new InitializeOptions { ProductName = "TickHappens", ProductVersion = "1.0" };
        Check(PlatformInterface.Initialize(ref init), "Initialize");
        try
        {
            string cache = config.CacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens", "eos");
            Directory.CreateDirectory(cache);
            var options = new Options
            {
                ProductId = config.ProductId,
                SandboxId = config.SandboxId,
                DeploymentId = config.DeploymentId,
                ClientCredentials = new ClientCredentials { ClientId = config.ClientId, ClientSecret = config.ClientSecret },
                CacheDirectory = cache,
                Flags = PlatformFlags.DisableOverlay,
                TickBudgetInMilliseconds = 3,
                TaskNetworkTimeoutSeconds = 15,
            };
            _platform = PlatformInterface.Create(ref options) ?? throw new InvalidOperationException("EOS platform creation failed.");
            _connect = _platform.GetConnectInterface();
            _lobby = _platform.GetLobbyInterface();
            _p2p = _platform.GetP2PInterface();
            SetRelayMode(forceRelays);
        }
        catch
        {
            _platform?.Release();
            PlatformInterface.Shutdown();
            throw;
        }
    }

    public void SetRelayMode(bool forceRelays)
    {
        var options = new SetRelayControlOptions
        {
            RelayControl = forceRelays ? RelayControl.ForceRelays : RelayControl.AllowRelays,
        };
        Check(_p2p.SetRelayControl(ref options), "SetRelayControl");
    }

    public Task SignInAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_local is not null) return Task.CompletedTask;
        Begin();
        var finished = new TaskCompletionSource();
        var options = new CreateDeviceIdOptions { DeviceModel = "Windows PC" };
        _connect.CreateDeviceId(ref options, null, (ref CreateDeviceIdCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            _completed.Enqueue(() =>
            {
                if (result is Result.Success or Result.DuplicateNotAllowed)
                    LoginDevice((status, user) =>
                    {
                        try
                        {
                            Check(status, "Device ID login");
                            _local = user ?? throw new InvalidOperationException("EOS returned no local user.");
                            LocalPeer = _local.ToString();
                            RegisterNotifications();
                            _busy = false;
                            finished.SetResult();
                        }
                        catch (Exception error)
                        {
                            UnregisterNotifications();
                            _local = null;
                            LocalPeer = null;
                            Fail(finished, error);
                        }
                    }, allowCreate: true);
                else Fail(finished, Error(result, "CreateDeviceId"));
            });
        });
        return finished.Task;
    }

    private void LoginDevice(Action<Result, ProductUserId?> done, bool allowCreate)
    {
        var options = new LoginOptions
        {
            Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken },
            UserLoginInfo = new UserLoginInfo { DisplayName = "TickHappens Player" },
        };
        _connect.Login(ref options, null, (ref LoginCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            var user = info.LocalUserId;
            var token = info.ContinuanceToken;
            _completed.Enqueue(() =>
            {
                if (allowCreate && result == Result.InvalidUser && token is not null)
                {
                    var create = new CreateUserOptions { ContinuanceToken = token };
                    _connect.CreateUser(ref create, null, (ref CreateUserCallbackInfo created) =>
                    {
                        Result status = created.ResultCode;
                        var createdUser = created.LocalUserId;
                        _completed.Enqueue(() => done(status, createdUser));
                    });
                }
                else done(result, user);
            });
        });
    }

    private void RegisterNotifications()
    {
        var members = new AddNotifyLobbyMemberStatusReceivedOptions();
        _memberNotification = _lobby.AddNotifyLobbyMemberStatusReceived(ref members, null,
            (ref LobbyMemberStatusReceivedCallbackInfo info) =>
            {
                if (_lobbyId is null || info.LobbyId.ToString() != _lobbyId) return;
                if (info.CurrentStatus == LobbyMemberStatus.Closed ||
                    (info.CurrentStatus == LobbyMemberStatus.Kicked && info.TargetUserId?.ToString() == LocalPeer))
                    _completed.Enqueue(HandleRoomClosed);
                else _refreshMembers = true;
            });
        var request = new AddNotifyPeerConnectionRequestOptions { LocalUserId = _local, SocketId = _socket };
        _requestNotification = _p2p.AddNotifyPeerConnectionRequest(ref request, null,
            (ref OnIncomingConnectionRequestInfo info) =>
            {
                var remote = info.RemoteUserId;
                if (info.SocketId?.SocketName != SocketName || remote is null) return;
                _completed.Enqueue(() =>
                {
                    RefreshMembers();
                    string id = remote.ToString();
                    if (IsAllowed(id)) Accept(remote);
                });
            });
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = _local, SocketId = _socket };
        _closedNotification = _p2p.AddNotifyPeerConnectionClosed(ref closed, null,
            (ref OnRemoteConnectionClosedInfo info) =>
            {
                var remote = info.RemoteUserId;
                if (info.SocketId?.SocketName != SocketName || remote is null) return;
                var reason = info.Reason;
                _completed.Enqueue(() =>
                {
                    string id = remote.ToString();
                    if (!IsAllowed(id)) return;
                    _connected.Remove(id);
                    // EOS can reopen the channel on the next send while the peer is still in the lobby.
                    // Membership changes and the co-op heartbeat detect when the peer actually leaves.
                    LogConnection($"EOS P2P connection closed ({reason}).");
                });
            });
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = _local, SocketId = _socket };
        _establishedNotification = _p2p.AddNotifyPeerConnectionEstablished(ref established, null,
            (ref OnPeerConnectionEstablishedInfo info) =>
            {
                var remote = info.RemoteUserId;
                if (info.SocketId?.SocketName != SocketName || remote is null) return;
                bool viaRelay = info.NetworkType == NetworkConnectionType.RelayedConnection;
                _completed.Enqueue(() =>
                {
                    string id = remote.ToString();
                    if (!IsAllowed(id)) return;
                    _connected.Add(id);
                    PeerConnected?.Invoke(id, viaRelay);
                });
            });
        var expiration = new AddNotifyAuthExpirationOptions();
        _expirationNotification = _connect.AddNotifyAuthExpiration(ref expiration, null,
            (ref AuthExpirationCallbackInfo info) =>
            {
                if (info.LocalUserId?.ToString() != LocalPeer) return;
                _completed.Enqueue(() => LoginDevice((result, _) =>
                {
                    if (result != Result.Success) HandleRoomClosed();
                }, allowCreate: false));
            });
        if (_memberNotification == 0 || _requestNotification == 0 ||
            _closedNotification == 0 || _establishedNotification == 0 || _expirationNotification == 0)
            throw new InvalidOperationException("EOS notification registration failed.");
        _receiveOptions = new ReceivePacketOptions
        {
            LocalUserId = _local, MaxDataSizeBytes = P2PInterface.MAX_PACKET_SIZE,
        };
    }

    public void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.Tick();
        long now = Stopwatch.GetTimestamp();
        if (_lobbyId is not null && (_refreshMembers || now >= _nextMembershipRefresh))
        {
            _refreshMembers = false;
            _nextMembershipRefresh = now + Stopwatch.Frequency;
            RefreshMembers();
        }
        while (_completed.TryDequeue(out var action)) action();
        if (_local is null) return;
        for (int packets = 0; packets < 256; packets++)
        {
            Result result = _p2p.ReceivePacket(ref _receiveOptions, ref _receivedUser, ref _receivedSocket,
                out byte channel, _incoming, out uint written);
            if (result != Result.Success)
            {
                if (result != Result.NotFound) LogP2pError("ReceivePacket", result, ref _lastReceiveError);
                break;
            }
            _lastReceiveError = null;
            if (channel != Channel || _receivedSocket.SocketName != SocketName || _lobbyId is null) continue;
            string? sender = null;
            foreach (var (id, member) in _members)
            {
                if (member != _receivedUser) continue;
                sender = id;
                break;
            }
            // EOS may return the same user as a different native handle.
            sender ??= _receivedUser.ToString();
            if (!IsAllowed(sender)) continue;
            if (!_receivedRoomPacket)
            {
                _receivedRoomPacket = true;
                Console.WriteLine("EOS P2P first room packet received.");
            }
            PacketReceived?.Invoke(sender, _incoming.AsMemory(0, (int)written));
        }
    }

    public void Send(string peerId, ReadOnlySpan<byte> payload, bool reliable)
    {
        if (payload.Length is < 1 or > P2PInterface.MAX_PACKET_SIZE)
            throw new ArgumentOutOfRangeException(nameof(payload));
        if (!IsAllowed(peerId)) return;
        payload.CopyTo(_outgoing);
        var options = new SendPacketOptions
        {
            LocalUserId = _local,
            RemoteUserId = _members[peerId],
            SocketId = _socket,
            Channel = Channel,
            Data = new ArraySegment<byte>(_outgoing, 0, payload.Length),
            AllowDelayedDelivery = reliable,
            DisableAutoAcceptConnection = false,
            Reliability = reliable ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered,
        };
        Result result = _p2p.SendPacket(ref options);
        LogP2pError("SendPacket", result, ref _lastSendError);
        if (result == Result.Success && !_sentRoomPacket)
        {
            _sentRoomPacket = true;
            Console.WriteLine("EOS P2P first room packet queued.");
        }
    }

    private bool IsAllowed(string peer) => _lobbyId is not null && _members.ContainsKey(peer) && (IsHost || peer == HostPeer);

    private void Accept(ProductUserId remote)
    {
        var options = new AcceptConnectionOptions { LocalUserId = _local, RemoteUserId = remote, SocketId = _socket };
        LogP2pError("AcceptConnection", _p2p.AcceptConnection(ref options), ref _lastAcceptError);
    }

    private static void LogP2pError(string operation, Result result, ref Result? previous)
    {
        if (result == Result.Success) return;
        if (result == previous) return;
        previous = result;
        LogConnection($"EOS P2P {operation} failed ({result}).");
    }

    internal static void LogConnection(string message)
    {
        Console.WriteLine(message);
        OnlineGame.WriteNetworkLog(message);
    }

    private void Close(ProductUserId remote)
    {
        var options = new CloseConnectionOptions { LocalUserId = _local, RemoteUserId = remote, SocketId = _socket };
        _p2p.CloseConnection(ref options);
    }

    private void Begin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Another EOS operation is in progress.");
        _busy = true;
    }

    private void Fail(TaskCompletionSource task, Exception error)
    {
        _busy = false;
        task.SetException(error);
    }

    private void Fail<T>(TaskCompletionSource<T> task, Exception error)
    {
        _busy = false;
        task.SetException(error);
    }

    private static InvalidOperationException Error(Result result, string operation) =>
        new($"EOS {operation} failed ({result}).");

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success) throw Error(result, operation);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterNotifications();
        ClearRoom();
        _platform.Release();
        PlatformInterface.Shutdown();
    }

    private void UnregisterNotifications()
    {
        if (_memberNotification != 0) _lobby.RemoveNotifyLobbyMemberStatusReceived(_memberNotification);
        if (_requestNotification != 0) _p2p.RemoveNotifyPeerConnectionRequest(_requestNotification);
        if (_closedNotification != 0) _p2p.RemoveNotifyPeerConnectionClosed(_closedNotification);
        if (_establishedNotification != 0) _p2p.RemoveNotifyPeerConnectionEstablished(_establishedNotification);
        if (_expirationNotification != 0) _connect.RemoveNotifyAuthExpiration(_expirationNotification);
        _memberNotification = _requestNotification = _closedNotification = _establishedNotification = _expirationNotification = 0;
    }
}
#endif
