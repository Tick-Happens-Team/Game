using System.Numerics;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class CoopWorldTests
{
    [Fact]
    public void FourPlayersHaveIndependentInputAndTheRunContinuesWhenHostDies()
    {
        var world = new RunWorld(TestContent.LoadCore(), playerCount: 4);
        for (int i = 1; i < 4; i++) world.SetPlayerActive(i, true);
        var spawn = world.Players.Select(p => p.Pos).ToArray();
        world.StepCoop([new(1, 0), new(-1, 0), new(0, 1, Dash: true), new(0, -1)]);

        Assert.True(world.Players[0].Pos.X > spawn[0].X);
        Assert.True(world.Players[1].Pos.X < spawn[1].X);
        Assert.True(world.Players[2].Pos.Y > spawn[2].Y);
        Assert.True(world.Players[2].DashTicks > 0);
        Assert.True(world.Players[3].Pos.Y < spawn[3].Y);

        world.Player.Hp = 0;
        world.SetPlayerActive(1, false);
        world.StepCoop([default, new(-1, 0), default, default]);
        Assert.Equal(RunPhase.Playing, world.Phase);
        Assert.False(world.Players[1].Active);
        world.SetPlayerActive(2, false);
        world.SetPlayerActive(3, false);
        world.StepCoop([default, default, default, default]);
        Assert.Equal(RunPhase.Dead, world.Phase);
    }

    [Fact]
    public void RemoteKillGoldAndChestPurchaseAreOwnedByThatPlayer()
    {
        var content = TestContent.LoadCore();
        content.Enemies[0].Gold = 25;
        content.Enemies[0].GoldChance = 1;
        var world = new RunWorld(content, playerCount: 4);
        world.SetPlayerActive(1, true);
        var remote = world.Players[1];
        remote.Pos = world.Chests[0];
        world.Player.Pos = remote.Pos;
        int enemy = world.SpawnEnemy(0, remote.Pos - new Vector2(0, 8));
        world.DamageEnemy(enemy, 10_000, DamageKind.Pure, new DamageSourceRef(1), remote.Pos.X, remote.Pos.Y);
        Assert.Equal(0, world.Player.Kills);
        Assert.Equal(1, remote.Kills);

        world.StepCoop([default, default, default, default]);
        Assert.True(world.ChestOpen[0]);
        Assert.True(remote.ChestOfferFirst >= 0);
        Assert.Equal(-1, world.Player.ChestOfferFirst);
        world.StepCoop([default, new RunInput(1, 0, ChestChoice: 2), default, default]);
        Assert.Equal(1, remote.ItemStacks.Sum());
        Assert.Equal(0, world.Player.ItemStacks.Sum());
        Assert.Equal(25 - content.Rules.Chests.StartPrice, remote.Wallet.Gold);
        Assert.Equal(0, world.Player.Wallet.Gold);
        Assert.True(remote.Wallet.NextPrice > world.Player.Wallet.NextPrice);
    }

    [Fact]
    public void CompleteSnapshotReplicatesStateAndMalformedPacketsAreAtomic()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, seed: 12, character: "octarine", playerCount: 4);
        host.SetPlayerActive(1, true);
        host.Players[1].Pos = host.Chests[0];
        host.Players[1].Wallet.Collect(new GoldDrop(100, 1), 1);
        int enemy = host.SpawnEnemy(0, new Vector2(610, 601));
        host.StepCoop([default, default, default, default]);
        var client = new RunWorld(content, seed: 7, character: "swordsman", playerCount: 4, localPlayerId: 1);
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.Equal(0, host.WriteSnapshot(snapshot.AsSpan(0, 3)));
        int size = host.WriteSnapshot(snapshot);
        Assert.True(size > 0);
        Assert.False(client.TryApplySnapshot(snapshot.AsSpan(0, size - 1)));
        Assert.Equal(7u, client.Seed);
        Assert.Equal(0, client.Tick);
        Assert.False(client.ChestOpen[0]);

        Assert.True(client.TryApplySnapshot(snapshot.AsSpan(0, size)));
        Assert.Equal(host.Seed, client.Seed);
        Assert.Equal(host.Tick, client.Tick);
        Assert.Equal(host.Player.Character.Id, client.Player.Character.Id);
        Assert.Equal(host.Players[1].Wallet.Gold, client.Player.Wallet.Gold);
        Assert.Equal(host.Players[1].ItemStacks, client.Player.ItemStacks);
        Assert.Equal(host.Players[1].ChestOfferFirst, client.Player.ChestOfferFirst);
        Assert.Equal(host.Players[1].ChestOfferSecond, client.Player.ChestOfferSecond);
        Assert.Equal(host.Players[1].ChestOfferUntilTick, client.Player.ChestOfferUntilTick);
        Assert.Equal(host.ChestOpen, client.ChestOpen);
        Assert.True(client.EAlive[enemy]);
        Assert.InRange(Vector2.Distance(client.Chests[0], host.Chests[0]), 0, 0.04f);
        Assert.InRange(Vector2.Distance(client.EnemyPos(enemy, 1), host.EnemyPos(enemy, 1)), 0, 0.04f);
        Assert.False(client.TryApplySnapshot(snapshot.AsSpan(0, size)));

        Assert.Throws<InvalidOperationException>(() => client.Step(default));
    }

    [Fact]
    public void AnEarlySnapshotContinuesRemoteInterpolationFromTheDisplayedPosition()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, playerCount: 2);
        var client = new RunWorld(content, playerCount: 2, localPlayerId: 1);
        var packet = new byte[RunWorld.SnapshotCapacity];
        Assert.True(client.TryApplySnapshot(packet.AsSpan(0, host.WriteSnapshot(packet))));
        host.StepCoop([new RunInput(1, 0), default]);
        Assert.True(client.TryApplySnapshot(packet.AsSpan(0, host.WriteSnapshot(packet))));
        var remote = client.Players[0];
        var displayed = Vector2.Lerp(remote.Prev, remote.Pos, 0.25f);
        host.StepCoop([new RunInput(1, 0), default]);
        Assert.True(client.TryApplySnapshot(packet.AsSpan(0, host.WriteSnapshot(packet)), previousAlpha: 0.25f));
        Assert.Equal(displayed, remote.Prev);
        Assert.True(remote.Pos.X > displayed.X);
    }

    [Fact]
    public void KillEventCarriesTheSeedUsedForTheAuthoritativeDrop()
    {
        var content = TestContent.LoadCore();
        content.Enemies[0].Gold = 25;
        content.Enemies[0].GoldChance = 1;
        var host = new RunWorld(content);
        int enemy = host.SpawnEnemy(0, host.Player.Pos);
        host.DamageEnemy(enemy, 10_000, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        var client = new RunWorld(content);
        Span<byte> packet = stackalloc byte[CoopSession.MaxEventBytes];
        int next = 0;
        int bytes = host.WriteEvents(packet, ref next);
        Assert.True(client.TryApplyEvents(packet[..bytes]));
        var death = Assert.Single(client.Events, e => e.Kind == EventKind.Kill);
        var rng = new SimRng(death.Seed);
        Assert.InRange(rng.Next(), 0, 1);
        int gold = Array.FindIndex(host.GKind, kind => kind == PickupKind.Gold);
        Assert.True(gold >= 0);
        Assert.Equal(host.GOwner[gold], death.B);
        Assert.Equal(host.GX[gold], death.X + rng.Range(-3, 3));
        Assert.Equal(host.GY[gold], death.Y + rng.Range(-3, 3));
    }

    [Fact]
    public void PredictionAdvancesOnlyLocalMovementAndMatchesTheHost()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, playerCount: 2);
        host.SetPlayerActive(1, true);
        var client = new RunWorld(content, playerCount: 2, localPlayerId: 1);
        byte[] bytes = new byte[RunWorld.SnapshotCapacity];
        int size = host.WriteSnapshot(bytes);
        Assert.True(client.TryApplySnapshot(bytes.AsSpan(0, size)));
        Vector2 otherPlayer = client.Players[0].Pos;
        float health = client.Player.Hp;

        RunInput[] inputs = [default, new(1, -1, true)];
        host.StepCoop(inputs);
        client.PredictLocalMovement(inputs[1]);
        Assert.InRange(Vector2.Distance(host.Players[1].Pos, client.Player.Pos), 0, 0.05f);
        Assert.Equal(otherPlayer, client.Players[0].Pos);
        Assert.Equal(0, client.Tick);
        Assert.Empty(client.Events);
        Assert.Equal(health, client.Player.Hp);

        host.Players[1].LastAppliedInput = 1;
        size = host.WriteSnapshot(bytes);
        Assert.True(client.TryApplySnapshot(bytes.AsSpan(0, size)));
        Assert.Equal(1, client.Player.LastAppliedInput);
        Assert.True(client.Player.PrevDash);
        Assert.InRange(Vector2.Distance(host.Players[1].Pos, client.Player.Pos), 0, 0.05f);
    }

    [Fact]
    public void EventPacketsPreserveCombatDataAndChainPoints()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content);
        var client = new RunWorld(content);
        host.Events.Add(new GameEvent { Kind = EventKind.Hit, X = 42.5f, Value = 12.75f, A = 1, Seed = uint.MaxValue });
        host.ChainPoints.Add(new(10, 12));
        host.ChainPoints.Add(new(20, 22));
        host.Events.Add(new GameEvent { Kind = EventKind.Chain, A = 0, B = 2, Seed = 11 });

        int next = 0;
        var packet = new byte[CoopSession.MaxEventBytes];
        int length = host.WriteEvents(packet.AsSpan(0, 34), ref next);
        Assert.Equal(1, next);
        Assert.True(client.TryApplyEvents(packet.AsSpan(0, length)));
        Assert.Equal(host.Events[0], Assert.Single(client.Events));

        length = host.WriteEvents(packet, ref next);
        Assert.False(client.TryApplyEvents(packet.AsSpan(0, length - 1)));
        Assert.Single(client.Events);
        Assert.Empty(client.ChainPoints);
        Assert.True(client.TryApplyEvents(packet.AsSpan(0, length)));
        Assert.Equal(host.Events, client.Events);
        Assert.Equal(host.ChainPoints, client.ChainPoints);
    }

    [Fact]
    public void CoopInputsProduceIdenticalSnapshotsWithTheSameSeed()
    {
        var content = TestContent.LoadCore();
        var first = new RunWorld(content, seed: 54, playerCount: 4);
        var second = new RunWorld(content, seed: 54, playerCount: 4);
        for (int i = 1; i < 4; i++)
        {
            first.SetPlayerActive(i, true);
            second.SetPlayerActive(i, true);
        }
        var inputs = new RunInput[4];
        for (int tick = 0; tick < 1800; tick++)
        {
            inputs[0] = new(tick / 60 % 2 == 0 ? 1 : -1, 0);
            inputs[3] = new(0, tick / 60 % 2 == 0 ? 1 : -1, Dash: tick % 100 == 0);
            first.StepCoop(inputs);
            second.StepCoop(inputs);
            first.ClearEvents();
            second.ClearEvents();
        }
        byte[] a = new byte[RunWorld.SnapshotCapacity], b = new byte[RunWorld.SnapshotCapacity];
        int size = first.WriteSnapshot(a);
        Assert.Equal(size, second.WriteSnapshot(b));
        Assert.True(size > 0);
        Assert.True(a.AsSpan(0, size).SequenceEqual(b.AsSpan(0, size)));
    }
}
