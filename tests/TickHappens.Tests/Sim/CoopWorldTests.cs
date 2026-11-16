using System.Numerics;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class CoopWorldTests
{
    [Fact]
    public void GuestDamageReplicatesTheDamagedPlayerSlot()
    {
        var content = TestContent.LoadCore();
        content.Enemies[0].Speed = 0;
        var host = new RunWorld(content, playerCount: 2);
        host.SetPlayerActive(1, true);
        var guest = host.Players[1];
        guest.Pos = host.Player.Pos + new Vector2(200, 0);
        host.SpawnEnemy(0, guest.Pos);
        host.StepCoop([default, default]);

        var client = new RunWorld(content, playerCount: 2, localPlayerId: 1);
        Span<byte> events = stackalloc byte[CoopSession.MaxEventBytes];
        int next = 0;
        Assert.True(client.TryApplyEvents(events[..host.WriteEvents(events, ref next)]));

        var hurt = Assert.Single(client.Events, e => e.Kind == EventKind.PlayerHurt);
        Assert.Equal(client.Player.Id, hurt.A);
        Assert.NotEqual(host.Player.Id, hurt.A);
    }

    [Fact]
    public void AllyRevivalRequiresContinuousPresenceInRadiusAndReplicates()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, playerCount: 2);
        host.SetPlayerActive(1, true);
        host.GiveItem(content.ItemIndex("dumbbell"));
        var fallen = host.Players[1];
        fallen.Hp = 0;
        var corpse = fallen.Pos;
        RunInput[] inputs = [default, default];
        host.Player.Pos = corpse - new Vector2(RunWorld.RevivalRadius + 1, 0);
        host.StepCoop(inputs);
        Assert.Equal(0, fallen.RevivalTicks);

        host.Player.Pos = corpse - new Vector2(RunWorld.RevivalRadius, 0);
        host.StepCoop(inputs);
        Assert.Equal(1, fallen.RevivalTicks);
        host.StepCoop([new RunInput(1, 0), default]);
        Assert.True(host.Player.Walking);
        Assert.Equal(2, fallen.RevivalTicks);
        host.Player.Pos = corpse - new Vector2(RunWorld.RevivalRadius + 1, 0);
        host.StepCoop(inputs);
        Assert.Equal(0, fallen.RevivalTicks);
        Assert.Equal(corpse, fallen.Pos);

        host.Player.Pos = corpse - new Vector2(RunWorld.RevivalRadius, 0);
        for (int i = 0; i < RunWorld.RevivalHoldTicks - 1; i++) host.StepCoop(inputs);
        Assert.Equal(0, fallen.Hp);
        Assert.Equal(RunWorld.RevivalHoldTicks - 1, fallen.RevivalTicks);
        var client = new RunWorld(content, playerCount: 2, localPlayerId: 1);
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(client.TryApplySnapshot(snapshot.AsSpan(0, host.WriteSnapshot(snapshot))));
        Assert.Equal(fallen.RevivalTicks, client.Player.RevivalTicks);

        host.StepCoop([new RunInput(1, 0), default]);
        TestWorld.Close(0.4f * (fallen.Character.Stats.MaxHp + 50), fallen.Hp);
        Assert.Equal(0, fallen.RevivalTicks);
        Assert.Equal(corpse, fallen.Pos);
        Assert.Equal(2 * RunWorld.TicksPerSecond, fallen.InvulnTicks);
        Assert.True(client.TryApplySnapshot(snapshot.AsSpan(0, host.WriteSnapshot(snapshot))));
        Assert.Equal(fallen.Hp, client.Player.Hp);
        Assert.Equal(fallen.InvulnTicks, client.Player.InvulnTicks);
        Assert.Equal(0, client.Player.RevivalTicks);

        Span<byte> events = stackalloc byte[CoopSession.MaxEventBytes];
        int next = 0;
        Assert.True(client.TryApplyEvents(events[..host.WriteEvents(events, ref next)]));
        var revival = Assert.Single(client.Events, e => e.Kind == EventKind.Revival);
        Assert.Equal(corpse, new Vector2(revival.X, revival.Y));
    }

    [Fact]
    public void GuestLevelUpReplicatesThePlayerSlotAndLevel()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, playerCount: 2);
        host.SetPlayerActive(1, true);
        var guest = host.Players[1];

        host.GX[0] = guest.Pos.X;
        host.GY[0] = guest.Pos.Y - 8;
        host.GKind[0] = PickupKind.Xp;
        host.GOwner[0] = guest.Id;
        host.GValue[0] = (int)guest.XpNext;
        host.GAlive[0] = true;
        host.StepCoop([default, default]);

        var client = new RunWorld(content, playerCount: 2, localPlayerId: 1);
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(client.TryApplySnapshot(snapshot.AsSpan(0, host.WriteSnapshot(snapshot))));
        Span<byte> events = stackalloc byte[CoopSession.MaxEventBytes];
        int next = 0;
        Assert.True(client.TryApplyEvents(events[..host.WriteEvents(events, ref next)]));

        var levelUp = Assert.Single(client.Events, e => e.Kind == EventKind.LevelUp);
        Assert.Equal(1, host.Player.Level);
        Assert.Equal(2, client.Player.Level);
        Assert.Equal(client.Player.Id, levelUp.A);
        Assert.Equal(client.Player.Level, levelUp.Value);
    }

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
        Assert.Equal(25, remote.Wallet.Gold);
        Assert.Equal(0, world.Player.Wallet.Gold);
        Assert.DoesNotContain(PickupKind.Gold, world.GKind);

        world.StepCoop([default, new RunInput(0, 0, OpenChest: true), default, default]);
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
    public void KillXpImmediatelyFliesPastTheCloserPlayerToTheKiller()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Enemies[0].Xp = 1;
        var host = new RunWorld(content, playerCount: 2);
        host.SetPlayerActive(1, true);
        var killer = host.Players[1];
        killer.Pos = host.Player.Pos + new Vector2(300, 0);
        var drop = host.Player.Pos + new Vector2(10, -8);
        int enemy = host.SpawnEnemy(0, drop);
        host.DamageEnemy(enemy, 10_000, DamageKind.Pure, new DamageSourceRef(1), killer.Pos.X, killer.Pos.Y);
        Assert.Equal(0, killer.Xp);

        host.StepCoop([default, default]);
        Assert.True(host.GX[0] > drop.X);
        var replica = new RunWorld(content, playerCount: 2);
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, host.WriteSnapshot(snapshot))));
        Assert.Equal(1, replica.GOwner[0]);

        host.SetPlayerActive(1, false);
        float beforeDisconnect = host.GX[0];
        host.StepCoop([default, default]);
        Assert.True(host.GAlive[0]);
        Assert.Equal(beforeDisconnect, host.GX[0]);
        host.RestoreCoopPlayer(1);
        for (int i = 0; i < 2 * RunWorld.TicksPerSecond; i++) host.StepCoop([default, default]);
        Assert.False(host.GAlive[0]);
        Assert.Equal(1, killer.Xp);
        Assert.Equal(0, host.Player.Xp);
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
        host.StepCoop([default, new RunInput(0, 0, OpenChest: true), default, default]);
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
    public void KillAndGoldAwardEffectsReplicate()
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
        var gold = Assert.Single(client.Events, e => e.Kind == EventKind.Pickup && e.A == (int)PickupKind.Gold);
        Assert.Equal(25, gold.Value);
        Assert.Equal(gold.X, death.X + rng.Range(-3, 3));
        Assert.Equal(gold.Y, death.Y + rng.Range(-3, 3));
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
