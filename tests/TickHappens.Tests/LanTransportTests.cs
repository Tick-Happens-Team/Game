using System.Diagnostics;
using System.Net;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class LanTransportTests
{
    [Fact]
    public async Task PreMatchHeroChoicesInitializeBothWorldsAndHostPauseStopsBothClocks()
    {
        var content = TestContent.LoadCore();
        using var host = new OnlineGame(content, lanPort: 0);
        using var guest = new OnlineGame(content);
        using var third = new OnlineGame(content);
        host.ConnectLan(true, null, content.Characters[0].Id, 1);
        guest.ConnectLan(false, new IPEndPoint(IPAddress.Loopback, host.LanPort!.Value), content.Characters[1].Id, 1);

        async Task PumpUntil(Func<bool> done)
        {
            var timeout = Stopwatch.StartNew();
            while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                host.Update(RunWorld.Dt, default);
                guest.Update(RunWorld.Dt, default);
                third.Update(RunWorld.Dt, default);
                await Task.Delay(2);
            }
            Assert.True(done());
        }

        await PumpUntil(() => guest.CanReady);
        host.ToggleReady();
        guest.ToggleReady();
        await PumpUntil(() => host.EveryoneReady && guest.LocalReady);
        host.Start();
        Assert.False(host.Playing);
        host.ToggleReady();
        guest.ToggleReady();
        await PumpUntil(() => !guest.LocalReady);

        third.ConnectLan(false, new IPEndPoint(IPAddress.Loopback, host.LanPort.Value), content.Characters[0].Id, 1);
        await PumpUntil(() => third.CanReady && guest.PlayerHero(2) == 2);
        Assert.Equal(2, third.SelectedHero);
        third.EditProfile(0, "Third");
        Assert.Equal(2, third.SelectedHero);

        guest.EditProfile(3, "Guest");
        await PumpUntil(() => host.PlayerHero(1) == 3);
        host.EditProfile(1, "Host");
        await PumpUntil(() => guest.PlayerHero(0) == 1 && guest.PlayerHero(1) == 3);
        Assert.Equal("Guest", guest.PlayerName(1));
        host.ToggleReady();
        guest.ToggleReady();
        await PumpUntil(() => host.PlayerReady(1));
        Assert.False(host.CanStart);
        third.ToggleReady();
        await PumpUntil(() => host.CanStart);
        host.Start();
        await PumpUntil(() => guest.Playing);
        Assert.Equal(content.Characters[1], guest.World!.Players[0].Character);
        Assert.Equal(content.Characters[3], guest.World.Players[1].Character);
        Assert.Equal(content.Characters[2], guest.World.Players[2].Character);

        host.SetPaused(true);
        await PumpUntil(() => guest.Paused);
        int tick = host.World!.Tick;
        host.Update(0.09f, default);
        guest.Update(0.09f, default);
        Assert.Equal(tick, host.World.Tick);
        Assert.Equal(3, host.PlayerCount);

        host.SetPaused(false);
        await PumpUntil(() => !guest.Paused && guest.World.Tick > tick);
    }

    [Fact]
    public async Task WipedTeamReturnsToSameRoomAndStartsAnotherRun()
    {
        var content = TestContent.LoadCore();
        using var host = new OnlineGame(content, lanPort: 0);
        using var guest = new OnlineGame(content);
        using var third = new OnlineGame(content);
        host.ConnectLan(true, null, "octarine", 1);
        int port = host.LanPort!.Value;
        guest.ConnectLan(false, new IPEndPoint(IPAddress.Loopback, port), "smith", 1);

        async Task PumpUntil(Func<bool> done)
        {
            var timeout = Stopwatch.StartNew();
            while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                host.Update(RunWorld.Dt, default);
                guest.Update(RunWorld.Dt, default);
                third.Update(RunWorld.Dt, default);
                await Task.Delay(2);
            }
            Assert.True(done());
        }

        await PumpUntil(() => guest.CanReady);
        third.ConnectLan(false, new IPEndPoint(IPAddress.Loopback, port), "miner", 1);
        await PumpUntil(() => third.CanReady);
        guest.EditProfile(content.CharacterIndex("swordsman"), "Guest");
        await PumpUntil(() => guest.SelectedHero == content.CharacterIndex("swordsman"));
        await ReadyAndStart();
        await PumpUntil(() => third.Playing);
        await PumpUntil(() => guest.Playing);
        var firstRun = host.World!;
        var firstReplica = guest.World!;
        firstRun.Players[0].Hp = firstRun.Players[1].Hp = firstRun.Players[2].Hp = 0;
        await PumpUntil(() => guest.World?.Phase == RunPhase.Dead);
        await PumpUntil(() => !host.Playing && !guest.Playing && !third.Playing);
        Assert.False(host.CanStart);
        Assert.False(guest.LocalReady);
        Assert.False(third.LocalReady);

        Assert.True(host.InRoom && guest.InRoom);
        Assert.Equal(port, host.LanPort);
        Assert.Equal(1, guest.LocalSlot);
        Assert.Equal("Guest", guest.PlayerName(1));
        guest.EditProfile(content.CharacterIndex("diver"), "Guest");
        await PumpUntil(() => guest.SelectedHero == content.CharacterIndex("diver"));
        await ReadyAndStart();
        await PumpUntil(() => guest.Playing && guest.World!.Tick > 0);

        Assert.NotSame(firstRun, host.World);
        Assert.NotSame(firstReplica, guest.World);
        Assert.Equal("diver", guest.World!.Player.Character.Id);
        Assert.True(guest.World.Player.Hp > 0);

        async Task ReadyAndStart()
        {
            host.ToggleReady();
            guest.ToggleReady();
            third.ToggleReady();
            await PumpUntil(() => host.CanStart);
            host.Start();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinsAndExchangesGameplayPacketsOverUdp(bool discover)
    {
        using var server = new LanTransport(host: true, port: 0);
        using var guest = new LanTransport(host: false);
        LanTransport activeGuest = guest;
        var join = discover
            ? guest.DiscoverAsync(server.Port)
            : guest.JoinAsync(new IPEndPoint(IPAddress.Loopback, server.Port));
        async Task PumpUntil(Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done() && timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (server.Port != 0) server.Pump();
                if (activeGuest.Port != 0) activeGuest.Pump();
                await Task.Delay(2);
            }
            Assert.True(done());
        }

        await PumpUntil(() => join.IsCompleted);
        await join;
        string digest = new('0', 64);
        using var host = new CoopSession(server, digest);
        Guid resumeKey = Guid.NewGuid();
        using var client = new CoopSession(guest, digest, Assert.IsType<string>(guest.HostPeer), resumeKey: resumeKey);
        client.Join();
        await PumpUntil(() => client.LocalPlayerSlot.HasValue);
        Assert.Equal(2, host.PlayerCount);

        RunInput? remoteInput = null;
        host.InputReceived += (_, _, input) => remoteInput = input;
        var move = new RunInput(1, -1, true);
        client.SubmitInput(move);
        await PumpUntil(() => remoteInput.HasValue);
        Assert.Equal(move, remoteInput);

        byte[] snapshot = new byte[2300];
        Random.Shared.NextBytes(snapshot);
        byte[]? received = null;
        client.SnapshotReceived += (_, data) => received = data.ToArray();
        host.BroadcastSnapshot(1, snapshot);
        await PumpUntil(() => received is not null);
        Assert.Equal(snapshot, received);

        byte[]? events = null;
        client.EventsReceived += data => events = data.ToArray();
        host.BroadcastEvents(snapshot.AsSpan(0, CoopSession.MaxEventBytes));
        await PumpUntil(() => events is not null);
        Assert.Equal(snapshot[..CoopSession.MaxEventBytes], events);

        host.AcceptNewPlayers = false;
        int? resumedSlot = null;
        host.PeerResumed += slot => resumedSlot = slot;
        var address = Assert.IsType<IPEndPoint>(guest.HostAddress);
        guest.Dispose();
        await PumpUntil(() => host.PlayerCount == 1);

        using var reconnected = new LanTransport(host: false);
        activeGuest = reconnected;
        var rejoin = reconnected.JoinAsync(address);
        await PumpUntil(() => rejoin.IsCompleted);
        await rejoin;
        using var returningClient = new CoopSession(reconnected, digest, Assert.IsType<string>(reconnected.HostPeer),
            resumeKey: resumeKey, resume: true);
        returningClient.Join();
        await PumpUntil(() => returningClient.LocalPlayerSlot.HasValue);
        Assert.Equal(1, resumedSlot);
        Assert.Equal(1, returningClient.LocalPlayerSlot);
        remoteInput = null;
        returningClient.SubmitInput(new RunInput(-1, 0));
        await PumpUntil(() => remoteInput.HasValue);
        Assert.Equal(new RunInput(-1, 0), remoteInput);

        bool disconnected = false;
        returningClient.HostDisconnected += () => disconnected = true;
        server.Dispose();
        await PumpUntil(() => disconnected);
        Assert.Equal("online.hostClosed", returningClient.JoinError);
    }
}
