using System.Numerics;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class BossAndChestTests
{
    private static ContentSet QuietContent()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        return content;
    }

    [Fact]
    public void FiveMinuteBossWavesRotateThenSpawnPairsWithOneRewardEach()
    {
        var world = new RunWorld(QuietContent(), seed: 56);
        string[] order = ["devourer", "timekeeper", "phantom", "prospector", "ancient_cultist"];
        for (int wave = 0; wave < order.Length * 2; wave++)
        {
            int atTick = (wave + 1) * 5 * 60 * RunWorld.TicksPerSecond;
            while (world.Tick < atTick - 1) world.Step(default);
            Assert.Equal(0, world.AliveEnemies);
            world.Step(default);

            int expected = wave < order.Length ? 1 : 2;
            int def = world.Content.EnemyIndex(order[wave % order.Length]);
            var slots = Enumerable.Range(0, world.EnemyHighWater).Where(i => world.EAlive[i]).ToArray();
            Assert.Equal(expected, slots.Length);
            var item = Assert.Single(world.Content.Items, item => item.BossDrop == order[wave % order.Length]);
            int reward = world.Content.ItemIndex(item.Id);
            int oldDrops = CountRewards(world, reward);
            int oldChests = CountBossChests(world);
            foreach (int slot in slots)
            {
                Assert.Equal(def, world.EDef[slot]);
                var source = new DamageSourceRef(world.Player.Id);
                world.DamageEnemy(slot, 1_000_000, DamageKind.Pure, source, 0, 0);
                world.DamageEnemy(slot, 1_000_000, DamageKind.Pure, source, 0, 0);
            }
            Assert.Equal(oldDrops + expected, CountRewards(world, reward));
            Assert.Equal(oldChests + expected, CountBossChests(world));
            world.ClearEvents();
        }
    }

    [Fact]
    public void BossChestOpensOnceForFreeAndIsReplicated()
    {
        var world = new RunWorld(QuietContent(), playerCount: 2);
        world.SetPlayerActive(1, true);
        var remote = world.Players[1];
        int boss = world.SpawnEnemy(world.Content.EnemyIndex("devourer"), remote.Pos);
        world.DamageEnemy(boss, 1_000_000, DamageKind.Pure, new DamageSourceRef(1), remote.Pos.X, remote.Pos.Y);
        int chest = Assert.Single(Enumerable.Range(0, world.Chests.Length), i => world.IsBossChest(i) && !world.ChestOpen[i]);
        world.Step(default);

        var replica = new RunWorld(world.Content, playerCount: 2, localPlayerId: 1);
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
        Assert.False(replica.ChestOpen[chest]);
        Assert.InRange(Vector2.Distance(world.Chests[chest], replica.Chests[chest]), 0, 0.04f);
        Assert.Equal(chest, replica.NearbyChest);

        var price = remote.Wallet.NextPrice;
        var goldBefore = remote.Wallet.Gold;
        world.StepCoop([default, new RunInput(0, 0, OpenChest: true)]);
        Assert.True(world.ChestOpen[chest]);
        Assert.True(remote.ChestOfferFirst >= 0);
        Assert.Equal(goldBefore, remote.Wallet.Gold);
        Assert.Equal(price, remote.Wallet.NextPrice);
        world.StepCoop([default, new RunInput(0, 0, ChestChoice: 1)]);
        for (int tick = 0; tick <= 60 * RunWorld.TicksPerSecond; tick++) world.Step(default);
        Assert.True(world.ChestOpen[chest]);
    }

    [Fact]
    public void HiddenChestIsFreeAndOffersRareOrBetterItems()
    {
        var world = new RunWorld(QuietContent());
        int chest = Assert.Single(Enumerable.Range(0, world.Chests.Length), world.IsRareChest);
        world.Player.Pos = world.Chests[chest];
        int price = world.Player.Wallet.NextPrice;
        var loop = new RunLoop(world);
        loop.Advance(1d / 60, new RunInput(0, 0, OpenChest: true));
        Assert.False(world.ChestOpen[chest]);
        loop.Advance(1d / 60, default);
        Assert.True(world.ChestOpen[chest]);
        Assert.NotEqual("common", world.Content.Items[world.Player.ChestOfferFirst].Rarity);
        Assert.NotEqual("cursed", world.Content.Items[world.Player.ChestOfferSecond].Rarity);
        Assert.Equal(0, world.Player.Wallet.Gold);
        Assert.Equal(price, world.Player.Wallet.NextPrice);
    }

    [Fact]
    public void ImmediateGoldAwardsGainOneCoinPerWholeMinute()
    {
        var content = QuietContent();
        content.Enemies[0].GoldChance = 1;
        var world = new RunWorld(content);
        int enemy = world.SpawnEnemy(0, new Vector2(1200, 1100));
        world.DamageEnemy(enemy, 100_000, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        int firstAward = world.Player.Wallet.Gold;
        Assert.Equal(content.Enemies[0].Gold, firstAward);
        for (int i = 0; i < 2 * 60 * RunWorld.TicksPerSecond; i++) world.Step(default);
        enemy = world.SpawnEnemy(0, new Vector2(1200, 1100));
        world.DamageEnemy(enemy, 100_000, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        Assert.Equal(content.Enemies[0].Gold + 2, world.Player.Wallet.Gold - firstAward);
    }

    [Fact]
    public void ThirdSpawnFlanksAMovingPlayerButBossWithSameSpawnIdDoesNot()
    {
        static float LateralMotion(bool boss)
        {
            var content = QuietContent();
            content.Enemies[0].Boss = boss;
            var world = new RunWorld(content);
            var player = world.Player.Pos;
            world.SpawnEnemy(0, player + new Vector2(0, 500));
            world.SpawnEnemy(0, player - new Vector2(0, 500));
            int slot = world.SpawnEnemy(0, player - new Vector2(170, 0));
            world.Step(new RunInput(1, 0));
            return world.EY[slot] - player.Y;
        }

        Assert.NotEqual(0, LateralMotion(false));
        Assert.Equal(0, LateralMotion(true));
    }

    [Fact]
    public void ArthursBasicAttackSlashesTheEnemiesInFrontWithoutFiringABolt()
    {
        var content = QuietContent();
        content.Enemies[0].Speed = 0;
        var world = new RunWorld(content, character: "king_arthur");
        world.Player.Facing = Vector2.UnitX;
        int front = world.SpawnEnemy(0, world.Player.Pos + new Vector2(28, 0));
        int back = world.SpawnEnemy(0, world.Player.Pos - new Vector2(28, 0));
        Assert.Equal(20, world.Player.Stats[Stat.AttackSpeed]);

        world.Step(default);

        Assert.False(world.EAlive[front]);
        Assert.True(world.EAlive[back]);
        Assert.DoesNotContain(world.PAlive, active => active);
        Assert.Contains(world.Events, e => e.Kind == EventKind.Melee && e.Value == 30);
    }

    private static int CountRewards(RunWorld world, int item) => Enumerable.Range(0, RunWorld.PickupCapacity)
        .Count(i => world.GAlive[i] && world.GKind[i] == PickupKind.Cursed && world.GValue[i] == item);

    private static int CountBossChests(RunWorld world) => Enumerable.Range(0, world.Chests.Length)
        .Count(i => world.IsBossChest(i) && !world.ChestOpen[i]);
}
