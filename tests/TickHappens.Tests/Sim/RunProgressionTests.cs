using System.Numerics;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class RunProgressionTests
{
    [Fact]
    public void LevelUpGrowsTableStatsAndHealsByTheIncrease()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1, character: "octarine");
        var p = world.Player;
        var table = p.Character.Stats;
        p.Hp = 100f;

        TestWorld.LevelTo(world, 5);

        Assert.Equal(5, p.Level);
        Assert.Equal(table.MaxHp + 4 * table.MaxHpPerLevel, p.Stats.Base(Stat.MaxHp));
        Assert.Equal(table.Damage + 4 * table.DamagePerLevel, p.Stats.Base(Stat.Damage));
        TestWorld.Close(170f, world.MaxHealth);
        TestWorld.Close(120f, p.Hp); // 100 + the 20 max HP gained on the way to level 5
    }

    [Fact]
    public void EnemyXpDropGrowsWithElapsedRunMinutesAndRetainsItsOwner()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        content.Waves.BossIntervalMinutes = 0;
        var world = new RunWorld(content);
        int enemyDef = content.EnemyIndex("amberslime");

        AssertDrop(10);
        for (int i = 0; i < 4 * 60 * RunWorld.TicksPerSecond; i++) world.Step(default);
        AssertDrop(12);

        void AssertDrop(int expected)
        {
            var position = world.Player.Pos + new Vector2(40, 0);
            int enemy = world.SpawnEnemy(enemyDef, position);
            world.DamageEnemy(enemy, 10_000, DamageKind.Pure, new DamageSourceRef(0), position.X, position.Y);
            int pickup = Assert.Single(Enumerable.Range(0, RunWorld.PickupCapacity),
                i => world.GAlive[i] && world.GKind[i] == PickupKind.Xp);
            Assert.Equal(expected, world.GValue[pickup]);
            Assert.Equal(world.Player.Id, world.GOwner[pickup]);
        }
    }

    [Fact]
    public void OctarineCooldownPassiveScalesWithLevel()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1, character: "octarine");

        TestWorld.Close(1f - 0.15f, world.Player.Stats[Stat.Cooldown]);

        TestWorld.LevelTo(world, 5);

        TestWorld.Close(1f - 0.15f - 0.0015f * 4, world.Player.Stats[Stat.Cooldown]);
    }

    [Fact]
    public void SwordsmanArmorPassiveScalesWithLevel()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1, character: "swordsman");

        TestWorld.Close(0.10f, world.Player.Stats[Stat.Armor]);

        TestWorld.LevelTo(world, 5);

        TestWorld.Close(0.10f + 0.01f * 4, world.Player.Stats[Stat.Armor]);
    }

    [Fact]
    public void GoflinGoldGainPassiveScalesWithLevel()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1, character: "goflin");

        TestWorld.Close(1f + 0.10f, world.Player.Stats[Stat.GoldGain]);

        TestWorld.LevelTo(world, 5);

        TestWorld.Close(1f + 0.10f + 0.0075f * 4, world.Player.Stats[Stat.GoldGain]);
    }

    [Fact]
    public void CelestialMageTradesHealthForMagicalDamage()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1, character: "celestial_mage");
        var p = world.Player;

        TestWorld.Close(p.Character.Stats.MaxHp * 0.35f, world.MaxHealth);
        TestWorld.Close(world.MaxHealth, p.Hp);
        TestWorld.Close(1.25f, p.Stats[Stat.MagicalDamage]);
        Assert.True(ContentLoader.TryParseDamage(p.Attack.DamageType, out var kind));
        Assert.Equal(DamageKind.Magical, kind);
    }

    [Fact]
    public void WatchmanDamageGrowsWithRunMinutes()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 5, character: "watchman");
        var p = world.Player;
        TestWorld.Close(3.75f, p.Stats[Stat.Damage]);

        p.Pos = world.Statues[0];
        int ticks = (int)(content.Rules.Statue.HoldSeconds * RunWorld.TicksPerSecond) + 2;
        for (int i = 0; i < ticks; i++) world.Step(default);

        Assert.True(world.StatueActive[0], "standing on the statue must activate it");
        float minutes = world.Minutes;
        Assert.True(minutes >= content.Rules.Statue.Minutes, $"minutes {minutes} should include the statue bonus");
        TestWorld.Close(3.75f + 0.75f * minutes, p.Stats[Stat.Damage]);

        TestWorld.LevelTo(world, 2);

        TestWorld.Close(3.75f + 0.35f + 0.75f * minutes, p.Stats[Stat.Damage]);
        TestWorld.Close(0.01f, p.Stats[Stat.PhysicalCrit]);
        TestWorld.Close(0.01f, p.Stats[Stat.MagicalCrit]);
    }

    [Fact]
    public void ItemsStackAndHealOnMaxHealthGain()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1, character: "octarine");
        var p = world.Player;
        int sprats = content.ItemIndex("sprats");
        p.Hp = 100f;

        world.GiveItem(sprats);
        world.GiveItem(sprats);

        Assert.Equal(2, p.ItemStacks[sprats]);
        TestWorld.Close(196f, world.MaxHealth);
        TestWorld.Close(146f, p.Hp);
    }

    [Fact]
    public void HeavyShacklesReduceMoveSpeedAndAddMight()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1, character: "octarine");

        world.GiveItem(content.ItemIndex("heavy_shackles"));

        TestWorld.Close(15f, world.Player.Stats[Stat.MoveSpeed]);
        TestWorld.Close(1.25f, world.Player.Stats[Stat.Might]);
        TestWorld.Close(45f, world.MoveSpeedPx);
    }

    [Fact]
    public void GoflinCollectsMoreGoldWithoutRoundingLoss()
    {
        var content = TestContent.LoadCore();
        var goflin = new RunWorld(content, seed: 3, character: "goflin");
        var octarine = new RunWorld(content, seed: 3, character: "octarine");

        for (int i = 0; i < 10; i++)
        {
            TestWorld.DropGold(goflin, 1);
            goflin.Step(default);
            TestWorld.DropGold(octarine, 1);
            octarine.Step(default);
        }

        Assert.Equal(11, goflin.Player.Wallet.Gold); // floor(10 × 1.10): fractions are carried between pickups
        Assert.Equal(10, octarine.Player.Wallet.Gold);
        Assert.True(goflin.Player.Wallet.Gold > octarine.Player.Wallet.Gold);
    }

    [Fact]
    public void ChestChoiceGrantsOneItemWhileThePlayerKeepsMoving()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1);
        var p = world.Player;
        p.Wallet.Collect(new GoldDrop(100, p.Id), p.Id);

        p.Pos = world.Chests[0];
        world.Step(default);
        Assert.False(world.ChestOpen[0]);
        world.Step(new RunInput(0, 0, OpenChest: true));

        Assert.True(world.ChestOpen[0], "standing on an affordable chest must open it");
        Assert.Equal(0, p.ItemStacks.Sum());
        int selected = p.ChestOfferSecond;
        Assert.NotEqual(p.ChestOfferFirst, selected);
        var atChest = p.Pos;
        var loop = new RunLoop(world);
        loop.Advance(1d / 60, new RunInput(1, 0, ChestChoice: 2));
        loop.Advance(1d / 60, new RunInput(1, 0));

        Assert.True(p.Pos.X > atChest.X);
        Assert.Equal(selected, world.LastChestItem);
        Assert.Equal(1, p.ItemStacks.Sum());
        Assert.Equal(15, p.Wallet.NextPrice);
        Assert.InRange(world.LastChestItem, 0, content.Items.Count - 1);
        Assert.Equal(1, p.ItemStacks[world.LastChestItem]);
        Assert.Equal(100 - content.Rules.Chests.StartPrice, p.Wallet.Gold);
    }

    [Fact]
    public void ChestsBecomeAvailableIndividuallyOneMinuteAfterPurchase()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        var world = new RunWorld(content, seed: 1);
        var player = world.Player;
        const int minuteTicks = 60 * RunWorld.TicksPerSecond;

        player.Pos = world.Chests[0];
        var originalPosition = player.Pos;
        player.Wallet.Collect(new GoldDrop(player.Wallet.NextPrice, player.Id), player.Id);
        world.Step(new RunInput(0, 0, OpenChest: true));
        int firstOpenTick = world.Tick;
        Assert.True(world.ChestOpen[0]);
        world.Step(new RunInput(0, 0, ChestChoice: 1));

        player.Pos = world.Chests[1];
        player.Wallet.Collect(new GoldDrop(player.Wallet.NextPrice, player.Id), player.Id);
        world.Step(new RunInput(0, 0, OpenChest: true));
        int secondOpenTick = world.Tick;
        Assert.True(world.ChestOpen[1]);

        while (world.Tick < firstOpenTick + minuteTicks - 1) world.Step(default);
        Assert.True(world.ChestOpen[0]);
        world.Step(default);
        Assert.False(world.ChestOpen[0]);
        Assert.NotEqual(originalPosition, world.Chests[0]);
        Assert.True(world.ChestOpen[1]);

        player.Pos = world.Chests[0];
        player.Wallet.Collect(new GoldDrop(player.Wallet.NextPrice, player.Id), player.Id);
        world.Step(new RunInput(0, 0, OpenChest: true));
        Assert.True(world.ChestOpen[0]);
        Assert.True(player.ChestOfferFirst >= 0);

        while (world.Tick < secondOpenTick + minuteTicks) world.Step(default);
        Assert.False(world.ChestOpen[1]);
    }

    [Fact]
    public void ChestChoiceTimesOutToTheFirstItem()
    {
        var world = new RunWorld(TestContent.LoadCore());
        var player = world.Player;
        player.Wallet.Collect(new GoldDrop(world.Rules.Chests.StartPrice, player.Id), player.Id);
        player.Pos = world.Chests[0];
        world.Step(new RunInput(0, 0, OpenChest: true));
        int first = player.ChestOfferFirst;
        Assert.True(first >= 0);

        while (world.Tick < player.ChestOfferUntilTick - 1) world.Step(default);
        Assert.Equal(0, player.ItemStacks.Sum());
        world.Step(default);

        Assert.Equal(first, world.LastChestItem);
        Assert.Equal(1, player.ItemStacks.Sum());
        Assert.Equal(-1, player.ChestOfferFirst);
    }

    [Fact]
    public void RollChestItemOnlyReturnsWeightedRarities()
    {
        string dir = TestContent.CreateScratchCopy();
        try
        {
            TestContent.EditJson(dir, "items.json", root =>
            {
                var expander = root.AsArray().First(i => (string?)i!["id"] == "expander")!;
                expander["rarity"] = "mythic";
            });
            TestContent.EditJson(dir, "rules.json", root => root["chests"]!["rarityWeights"]!["mythic"] = 0);
            var content = ContentLoader.Load(dir);
            var world = new RunWorld(content, seed: 9);
            int expanderIndex = content.ItemIndex("expander");
            Assert.Equal("mythic", content.Items[expanderIndex].Rarity);

            for (int i = 0; i < 500; i++)
            {
                int item = world.RollChestItem();
                Assert.InRange(item, 0, content.Items.Count - 1);
                Assert.True(content.Rules.Chests.RarityWeights[content.Items[item].Rarity] > 0,
                    $"rolled '{content.Items[item].Id}' of rarity '{content.Items[item].Rarity}' with zero weight");
                Assert.NotEqual(expanderIndex, item);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CommonItemsRollFarMoreOftenThanLegendary()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 21);
        int common = 0, legendary = 0;

        for (int i = 0; i < 2000; i++)
        {
            var item = content.Items[world.RollChestItem()];
            if (item.Rarity == "common") common++;
            else if (item.Rarity == "legendary") legendary++;
        }

        Assert.True(common > 0 && legendary > 0, $"both rarities must appear: common {common}, legendary {legendary}");
        Assert.True(common > legendary * 5, $"common {common} should far outnumber legendary {legendary}");
    }

    [Fact]
    public void StatueHoldAddsBonusMinutes()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 5);
        var rules = content.Rules.Statue;
        float minutesBefore = world.Minutes;

        world.Player.Pos = world.Statues[0];
        int ticks = (int)(rules.HoldSeconds * RunWorld.TicksPerSecond) + 2;
        for (int i = 0; i < ticks; i++) world.Step(default);

        Assert.True(world.StatueActive[0], "standing on the statue for holdSeconds must activate it");
        Assert.Equal(rules.Minutes * 60f, world.BonusSeconds);
        Assert.True(world.Minutes >= minutesBefore + rules.Minutes,
            $"minutes {world.Minutes} should have jumped by {rules.Minutes}");
        Assert.True(world.Minutes <= minutesBefore + rules.Minutes + ticks * RunWorld.Dt / 60f + 0.001f,
            $"minutes {world.Minutes} jumped by more than one statue bonus");
    }

    [Fact]
    public void StatueTimeJumpTriggersEarlierElitesEvenWhenScheduleIsUnsorted()
    {
        string dir = TestContent.CreateScratchCopy();
        try
        {
            TestContent.EditJson(dir, "waves.json", root =>
            {
                var elites = root["elites"]!.AsArray();
                elites[0]!["at"] = 3;
                elites[1]!["at"] = 1.5f;
            });
            var content = ContentLoader.Load(dir);
            var world = new RunWorld(content, seed: 5);
            world.Player.Pos = world.Statues[0];

            int ticks = (int)(world.Rules.Statue.HoldSeconds * RunWorld.TicksPerSecond) + 2;
            for (int i = 0; i < ticks && !world.StatueActive[0]; i++) world.Step(default);
            Assert.True(world.StatueActive[0]);
            Assert.DoesNotContain(world.Events, e => e.Kind == EventKind.Elite);
            world.Step(default);

            var spawns = world.Events.Where(e => e.Kind == EventKind.Elite).ToArray();
            Assert.Equal(2, spawns.Length);
            var elite = content.Enemies[content.EnemyIndex("duskslime")];
            foreach (var spawn in spawns)
            {
                TestWorld.Close(elite.Hp * world.HpScale, world.EMaxHp[spawn.A]);
                TestWorld.Close(elite.Damage * world.DamageScale, world.EDamage[spawn.A]);
            }

            world.ClearEvents();
            world.Step(default);
            Assert.DoesNotContain(world.Events, e => e.Kind == EventKind.Elite);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DifficultyScalesWithRunTimeNotPlayerLevel()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 5);
        float before = world.HpScale;
        Assert.Equal(1f, before);

        world.Player.Pos = world.Statues[0];
        int ticks = (int)(content.Rules.Statue.HoldSeconds * RunWorld.TicksPerSecond) + 2;
        for (int i = 0; i < ticks; i++) world.Step(default);

        Assert.True(world.StatueActive[0], "the statue must be active before difficulty is compared");
        Assert.True(world.HpScale > before, $"hp scale {world.HpScale} should exceed {before} after the statue bonus");
        float minutes = world.Minutes;
        float linearScale = 1f + content.Waves.HpPerMinute * minutes;
        Assert.True(world.HpScale > linearScale);
        TestWorld.Close(linearScale + content.Waves.HpPerMinuteSquared * minutes * minutes, world.HpScale);

        float afterStatue = world.HpScale;
        world.GainXp(world.XpForLevel(1) * 3);
        Assert.True(world.Player.Level > 1, "the player should have levelled up");
        Assert.Equal(afterStatue, world.HpScale);

        content.Waves.HpPerMinuteSquared = 0;
        TestWorld.Close(linearScale, world.HpScale);
    }
}
