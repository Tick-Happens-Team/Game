using System.Net;
using LiteNetLib;
using TickHappens.Localization;

namespace TickHappens.Game.Online;

public sealed class LanTransport : ICoopTransport, IDisposable
{
    public const int DefaultPort = 39230;
    private const string ConnectionKey = "TickHappensCoop";
    private static readonly byte[] Query = "TickHappens/LAN?"u8.ToArray();
    private static ReadOnlySpan<byte> Reply => "TickHappens/LAN!"u8;
    private readonly NetManager _network;
    private readonly Dictionary<string, NetPeer> _peers = new();
    private readonly Dictionary<NetPeer, string> _ids = new();
    private TaskCompletionSource? _joining;
    private IPEndPoint? _discovered;
    private int _discoveryPort;
    private bool _ambiguous;
    private long _searchStarted;
    private int _broadcasts;

    public LanTransport(bool host, int port = DefaultPort)
    {
        var listener = new EventBasedNetListener();
        _network = new NetManager(listener)
        {
            AutoRecycle = true,
            BroadcastReceiveEnabled = host,
            UnconnectedMessagesEnabled = !host,
            IPv6Enabled = false,
            ReuseAddress = false,
        };
        listener.ConnectionRequestEvent += request =>
        {
            if (host && _peers.Count < CoopSession.MaxPlayers - 1)
                request.AcceptIfKey(ConnectionKey);
            else
                request.Reject();
        };
        listener.PeerConnectedEvent += peer =>
        {
            string id = peer.ToString();
            _ids.Add(peer, id);
            _peers.Add(id, peer);
            if (!host)
            {
                HostPeer = id;
                _joining?.TrySetResult();
            }
        };
        listener.PeerDisconnectedEvent += (peer, _) =>
        {
            if (_ids.Remove(peer, out string? id))
            {
                _peers.Remove(id);
                PeerDisconnected?.Invoke(id);
            }
            if (!host)
            {
                HostPeer = null;
                FailJoin("online.timeout");
            }
        };
        listener.NetworkReceiveEvent += (peer, reader, _, _) =>
        {
            if (reader.AvailableBytes <= CoopSession.MaxPacketBytes && _ids.TryGetValue(peer, out string? id))
                PacketReceived?.Invoke(id,
                    reader.RawData.AsMemory(reader.Position, reader.AvailableBytes));
        };
        listener.NetworkReceiveUnconnectedEvent += (endpoint, reader, type) =>
        {
            var bytes = reader.RawData.AsSpan(reader.Position, reader.AvailableBytes);
            if (host && Discoverable && _peers.Count < CoopSession.MaxPlayers - 1 &&
                type == UnconnectedMessageType.Broadcast && bytes.SequenceEqual(Query))
                _network.SendUnconnectedMessage(Reply, endpoint);
            if (!host && _discoveryPort != 0 && endpoint.Port == _discoveryPort &&
                type == UnconnectedMessageType.BasicMessage && bytes.SequenceEqual(Reply))
            {
                _ambiguous |= _discovered is not null && !_discovered.Equals(endpoint);
                _discovered = endpoint;
            }
        };
        if (!_network.Start(host ? port : 0))
            throw new LocalizedException("online.lanBind");
    }

    public string? HostPeer { get; private set; }
    public bool Discoverable { get; set; } = true;
    public int Port => _network.LocalPort;
    public event Action<string, ReadOnlyMemory<byte>>? PacketReceived;
    public event Action<string>? PeerDisconnected;

    public Task JoinAsync(IPEndPoint endpoint)
    {
        _joining = new TaskCompletionSource();
        _network.Connect(endpoint, ConnectionKey);
        return _joining.Task;
    }

    public Task DiscoverAsync(int port = DefaultPort)
    {
        _joining = new TaskCompletionSource();
        _searchStarted = Environment.TickCount64;
        _discoveryPort = port;
        return _joining.Task;
    }

    public void Pump()
    {
        _network.PollEvents();
        if (_discoveryPort == 0) return;
        long elapsed = Environment.TickCount64 - _searchStarted;
        if (elapsed >= 1000)
        {
            _discoveryPort = 0;
            if (_ambiguous) FailJoin("online.lanMultiple");
            else if (_discovered is null) FailJoin("online.lanNotFound");
            else _network.Connect(_discovered, ConnectionKey);
        }
        else if (elapsed >= _broadcasts * 250)
        {
            _broadcasts++;
            _network.SendBroadcast(Query, _discoveryPort);
        }
    }

    private void FailJoin(string key) => _joining?.TrySetException(new LocalizedException(key));

    public void Send(string peerId, ReadOnlySpan<byte> payload, bool reliable)
    {
        if (_peers.TryGetValue(peerId, out var peer))
            peer.Send(payload, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
    }

    public void Dispose()
    {
        _joining?.TrySetCanceled();
        _network.Stop();
    }
}
