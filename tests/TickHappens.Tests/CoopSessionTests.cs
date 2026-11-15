using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class CoopSessionTests
{
    private static readonly string Digest = new('0', 64);

    [Fact]
    public void FourPlayerSessionCarriesInputAndAuthoritativeWorldState()
    {
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest);
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, playerCount: 4);
        var inputs = new RunInput[4];
        host.PeerJoined += slot => world.SetPlayerActive(slot, true);
        host.InputReceived += (slot, _, input) => inputs[slot] = input;
        using var a = new CoopSession(wire.Connect("first"), Digest, "host");
        using var b = new CoopSession(wire.Connect("second"), Digest, "host");
        using var c = new CoopSession(wire.Connect("third"), Digest, "host");
        var clients = new[] { a, b, c };
        foreach (var client in clients) client.Join();
        wire.Flush();
        Assert.Equal(4, host.PlayerCount);
        host.AcceptNewPlayers = false;

        var views = clients.Select(client =>
            new RunWorld(content, playerCount: 4, localPlayerId: client.LocalPlayerSlot!.Value)).ToArray();
        for (int i = 0; i < clients.Length; i++)
        {
            var view = views[i];
            clients[i].SnapshotReceived += (_, bytes) => Assert.True(view.TryApplySnapshot(bytes.Span));
            clients[i].SubmitInput(new RunInput(i == 0 ? 1 : -1, 0, i == 2));
        }
        wire.Flush();
        world.StepCoop(inputs);
        Assert.True(world.Players[1].Pos.X > world.Players[1].Prev.X);
        Assert.True(world.Players[2].Pos.X < world.Players[2].Prev.X);
        Assert.True(world.Players[3].DashTicks > 0);
        byte[] data = new byte[RunWorld.SnapshotCapacity];
        int size = world.WriteSnapshot(data);
        host.BroadcastSnapshot(world.Tick, data.AsSpan(0, size));
        wire.Flush();
        foreach (var view in views)
        {
            Assert.Equal(world.Tick, view.Tick);
            Assert.All(view.Players, player => Assert.True(player.Active));
            for (int i = 0; i < view.Players.Count; i++)
                Assert.InRange(System.Numerics.Vector2.Distance(world.Players[i].Pos, view.Players[i].Pos), 0, 0.04f);
        }
    }

    [Fact]
    public void RoomAssignsThreeUniqueClientSlotsRejectsOverflowAndReleasesDisconnectedSlot()
    {
        var loopback = new Loopback();
        using var host = new CoopSession(loopback.Connect("host"), Digest);
        using var first = new CoopSession(loopback.Connect("first"), Digest, "host");
        using var second = new CoopSession(loopback.Connect("second"), Digest, "host");
        using var third = new CoopSession(loopback.Connect("third"), Digest, "host");
        using var extra = new CoopSession(loopback.Connect("extra"), Digest, "host");
        var joined = new List<int>();
        var left = new List<int>();
        host.PeerJoined += joined.Add;
        host.PeerLeft += left.Add;

        foreach (var peer in new[] { first, second, third, extra })
        {
            peer.Join();
            loopback.Flush();
        }
        Assert.Equal(0, host.LocalPlayerSlot);
        Assert.Equal(1, first.LocalPlayerSlot);
        Assert.Equal(2, second.LocalPlayerSlot);
        Assert.Equal(3, third.LocalPlayerSlot);
        Assert.Null(extra.LocalPlayerSlot);
        Assert.Equal("online.full", extra.JoinError);
        Assert.Equal([1, 2, 3], joined);

        loopback.Drop("second");
        extra.Join();
        loopback.Flush();
        Assert.Equal([2], left);
        Assert.Equal(2, extra.LocalPlayerSlot);
        loopback.Drop("host");
        Assert.Null(extra.LocalPlayerSlot);
        Assert.Equal("online.hostClosed", extra.JoinError);
    }

    [Fact]
    public void IncompatibleContentAndUnassignedInputAreRejected()
    {
        var loopback = new Loopback();
        var hostWire = loopback.Connect("host");
        var clientWire = loopback.Connect("client");
        using var host = new CoopSession(hostWire, Digest);
        using var client = new CoopSession(clientWire, new('1', 64), "host");
        host.PeerJoined += _ => throw new InvalidOperationException("Unexpected player join.");
        host.InputReceived += (_, _, _) => throw new InvalidOperationException("Unexpected input.");

        clientWire.Send("host", [CoopSession.ProtocolVersion, 4, 0, 0, 0, 0, 1, 0, 0, 0, 5], false);
        client.Join();
        loopback.Flush();

        Assert.Null(client.LocalPlayerSlot);
        Assert.Equal("online.incompatible", client.JoinError);
    }

    [Fact]
    public void RedundantInputRecoversLostDashAndIgnoresReplayOrMalformedPackets()
    {
        var loopback = new Loopback();
        using var host = new CoopSession(loopback.Connect("host"), Digest);
        var clientWire = loopback.Connect("client");
        using var client = new CoopSession(clientWire, Digest, "host");
        client.Join();
        loopback.Flush();
        var inputs = new List<(int Slot, int Sequence, RunInput Input)>();
        host.InputReceived += (slot, sequence, input) => inputs.Add((slot, sequence, input));

        client.SubmitInput(new RunInput(-1, 0, true));
        loopback.Pending.Clear();
        client.SubmitInput(new RunInput(0, 1));
        loopback.Pending.Add(loopback.Pending[0]);
        clientWire.Send("host", [CoopSession.ProtocolVersion, 4, 0, 0, 0, 0, 3, 0, 0, 0, 5, 5, 255], false);
        loopback.Flush();

        Assert.Equal([(1, 1, new RunInput(-1, 0, true)), (1, 2, new RunInput(0, 1))], inputs);
    }

    [Fact]
    public void RestartAcceptsLowTicksAndRejectsPacketsFromThePreviousRun()
    {
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest);
        using var guest = new CoopSession(wire.Connect("guest"), Digest, "host");
        guest.Join();
        wire.Flush();

        var ticks = new List<int>();
        int inputs = 0;
        guest.SnapshotReceived += (tick, _) => ticks.Add(tick);
        host.InputReceived += (_, _, _) => inputs++;
        host.SetRunActive(true);
        host.BroadcastSnapshot(80, [1]);
        wire.Flush();
        guest.SubmitInput(default);
        host.BroadcastSnapshot(81, [1]);
        var late = wire.Pending.ToArray();
        wire.Pending.Clear();

        host.SetRunActive(false);
        host.SetRunActive(true);
        wire.Flush();
        wire.Pending.AddRange(late);
        guest.SubmitInput(default);
        host.BroadcastSnapshot(1, [2]);
        wire.Flush();

        Assert.Equal([80, 1], ticks);
        Assert.Equal(1, inputs);
        Assert.Equal(2, host.PlayerCount);
    }

    [Fact]
    public void SnapshotAssemblesOutOfOrderAndIgnoresOlderIncompleteFrames()
    {
        var loopback = new Loopback();
        using var host = new CoopSession(loopback.Connect("host"), Digest);
        using var client = new CoopSession(loopback.Connect("client"), Digest, "host");
        client.Join();
        loopback.Flush();
        var snapshots = new List<(int Tick, byte[] Bytes)>();
        client.SnapshotReceived += (tick, snapshot) => snapshots.Add((tick, snapshot.ToArray()));
        byte[] payload = new byte[3200];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251);

        host.BroadcastSnapshot(10, payload);
        var late = loopback.Pending[0];
        loopback.Pending.RemoveAt(0);
        loopback.Flush();
        Assert.Empty(snapshots);

        host.BroadcastSnapshot(11, payload);
        loopback.Pending.Reverse();
        loopback.Pending.Insert(1, loopback.Pending[0]);
        loopback.Pending.Insert(2, late);
        loopback.Flush();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(11, snapshot.Tick);
        Assert.Equal(payload, snapshot.Bytes);
    }

    [Fact]
    public void NewerSnapshotDoesNotDiscardIncompleteReliableKeyframe()
    {
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest);
        using var client = new CoopSession(wire.Connect("client"), Digest, "host");
        client.Join();
        wire.Flush();
        byte[] keyframe = new byte[2300];
        byte[] delta = [1, 2, 3];
        var delivered = new List<(int Tick, byte[] Bytes)>();
        client.KeyframeReceived += (tick, bytes) => delivered.Add((tick, bytes.ToArray()));
        client.SnapshotReceived += (tick, bytes) => delivered.Add((tick, bytes.ToArray()));
        host.BroadcastKeyframe(3, keyframe);
        host.BroadcastSnapshot(6, delta);
        var newer = wire.Pending[^1];
        wire.Pending.RemoveAt(wire.Pending.Count - 1);
        wire.Pending.Insert(1, newer);
        wire.Flush();
        Assert.Equal(new[] { 6, 3 }, delivered.Select(item => item.Tick));
        Assert.Equal(delta, delivered[0].Bytes);
        Assert.Equal(keyframe, delivered[1].Bytes);
    }

    [Fact]
    public void RetriesHandshakeThenKeepsAnIdleRoomAliveUntilPeerTimesOut()
    {
        var clock = new ManualClock();
        var loopback = new Loopback();
        using var host = new CoopSession(loopback.Connect("host"), Digest, clock: clock);
        using var client = new CoopSession(loopback.Connect("client"), Digest, "host", clock);
        client.Join();
        loopback.Pending.Clear();

        clock.Seconds = 1;
        client.Poll();
        loopback.Flush();
        Assert.Equal(1, client.LocalPlayerSlot);
        Assert.Equal(2, host.PlayerCount);

        for (int i = 3; i <= 21; i += 2)
        {
            clock.Seconds = i;
            client.Poll();
            host.Poll();
            loopback.Flush();
        }
        Assert.Equal(1, client.LocalPlayerSlot);
        Assert.Equal(2, host.PlayerCount);

        clock.Seconds += 15;
        client.Poll();
        host.Poll();
        Assert.Null(client.LocalPlayerSlot);
        Assert.Equal(1, host.PlayerCount);
    }

    [Theory]
    [InlineData(59, true, true)]
    [InlineData(60, true, false)]
    [InlineData(1, false, false)]
    public void ResumeRequiresMatchingUnexpiredSlotKey(int elapsed, bool sameKey, bool accepted)
    {
        var clock = new ManualClock();
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest, clock: clock);
        Guid key = Guid.NewGuid();
        using var client = new CoopSession(wire.Connect("client"), Digest, "host", clock, resumeKey: key);
        client.Join();
        wire.Flush();
        Assert.Equal(1, client.LocalPlayerSlot);

        host.AcceptNewPlayers = false;
        wire.Drop("client");
        clock.Seconds = elapsed;
        using var resumed = new CoopSession(wire.Connect("new-endpoint"), Digest, "host", clock,
            resumeKey: sameKey ? key : Guid.NewGuid(), resume: true);
        resumed.Join();
        wire.Flush();
        Assert.Equal(accepted ? 1 : null, resumed.LocalPlayerSlot);
        Assert.Equal(accepted ? null : "online.resumeExpired", resumed.JoinError);
        Assert.Equal(accepted ? 2 : 1, host.PlayerCount);
    }

    [Fact]
    public void HostSynchronizesProfilesAndPauseWithoutAllowingGuestPauseOrMidRunChanges()
    {
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest, characterCount: 3);
        using var guest = new CoopSession(wire.Connect("guest"), Digest, "host", characterCount: 3);
        host.SetProfile(0, "Host");
        guest.SetProfile(2, "Ёж-2");
        guest.Join();
        wire.Flush();
        Assert.Equal(new CoopSession.PlayerProfile(2, "Ёж-2"), host.Profile(1));
        Assert.Equal(host.Profile(1), guest.Profile(1));
        Assert.Equal(host.Profile(0), guest.Profile(0));

        guest.SetProfile(3, "WrongHero");
        guest.SetProfile(0, "🙂");
        host.AcceptNewPlayers = false;
        guest.SetProfile(1, "TooLate");
        guest.SetPaused(true);
        wire.Flush();
        Assert.Equal(new CoopSession.PlayerProfile(2, "Ёж-2"), host.Profile(1));
        Assert.False(guest.Paused);

        host.SetPaused(true);
        wire.Flush();
        Assert.True(guest.Paused);
        host.SetPaused(false);
        wire.Flush();
        Assert.False(guest.Paused);
    }

    [Fact]
    public void HostRejectsAConcurrentSelectionOfTheSameHero()
    {
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest, characterCount: 3);
        using var guest = new CoopSession(wire.Connect("guest"), Digest, "host", characterCount: 3);
        host.SetProfile(0, "Host");
        guest.SetProfile(1, "Guest");
        guest.Join();
        wire.Flush();

        host.SetProfile(2, "Host");
        guest.SetProfile(2, "Guest"); // The guest hasn't received the host's new choice yet.
        wire.Flush();

        Assert.Equal(2, host.Profile(0)?.Hero);
        Assert.Equal(1, host.Profile(1)?.Hero);
        Assert.Equal(host.Profile(1), guest.Profile(1));
    }

    [Fact]
    public void GuestCanWaitInLobbyAndTimesOutOnlyAfterHostStopsResponding()
    {
        var clock = new ManualClock();
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest, clock: clock);
        using var guest = new CoopSession(wire.Connect("guest"), Digest, "host", clock);
        guest.Join();
        wire.Flush();

        for (clock.Seconds = 2; clock.Seconds <= 20; clock.Seconds += 2)
        {
            host.Poll();
            guest.Poll();
            wire.Flush();
        }
        Assert.Equal(1, guest.LocalPlayerSlot);
        Assert.Equal(2, host.PlayerCount);
        Assert.Null(guest.JoinError);

        clock.Seconds = 35;
        guest.Poll();
        Assert.Null(guest.LocalPlayerSlot);
        Assert.Equal("online.timeout", guest.JoinError);
    }

    [Fact]
    public void MeasuresRoundTripTimeAndClearsItWhenPeerDisconnects()
    {
        var clock = new ManualClock();
        var wire = new Loopback();
        using var host = new CoopSession(wire.Connect("host"), Digest, clock: clock);
        using var guest = new CoopSession(wire.Connect("guest"), Digest, "host", clock);
        guest.Join();
        wire.Flush();
        Assert.Null(host.Ping(1));
        clock.Seconds = 2;
        host.Poll();
        clock.Seconds = 3;
        wire.Flush();
        Assert.Equal(1000, host.Ping(1));
        wire.Drop("guest");
        Assert.Null(host.Ping(1));
    }

    private sealed class ManualClock : TimeProvider
    {
        public int Seconds { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }

    private sealed class Loopback
    {
        private readonly Dictionary<string, Endpoint> _peers = new(StringComparer.Ordinal);
        public readonly List<(string From, string To, byte[] Packet)> Pending = [];

        public Endpoint Connect(string id)
        {
            var endpoint = new Endpoint(this, id);
            _peers.Add(id, endpoint);
            return endpoint;
        }

        public void Drop(string id)
        {
            _peers.Remove(id);
            foreach (var peer in _peers.Values) peer.Disconnect(id);
        }

        public void Flush()
        {
            while (Pending.Count > 0)
            {
                var message = Pending[0];
                Pending.RemoveAt(0);
                _peers[message.To].Receive(message.From, message.Packet);
            }
        }

        public sealed class Endpoint(Loopback owner, string id) : ICoopTransport
        {
            public event Action<string, ReadOnlyMemory<byte>>? PacketReceived;
            public event Action<string>? PeerDisconnected;

            public void Send(string peerId, ReadOnlySpan<byte> payload, bool reliable)
            {
                Assert.InRange(payload.Length, 1, CoopSession.MaxPacketBytes);
                owner.Pending.Add((id, peerId, payload.ToArray()));
            }

            public void Receive(string id, byte[] packet) => PacketReceived?.Invoke(id, packet);
            public void Disconnect(string id) => PeerDisconnected?.Invoke(id);
        }
    }
}
