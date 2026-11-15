using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class CombatTests
{
    [Fact]
    public void ResistUsesArmorForPhysicalMagicResistForMagicalAndNothingForPure()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1);

        TestWorld.Close(0.3f, world.Resist(DamageKind.Physical, 0.3f, 0.7f));
        TestWorld.Close(0.7f, world.Resist(DamageKind.Magical, 0.3f, 0.7f));
        TestWorld.Close(0f, world.Resist(DamageKind.Pure, 0.3f, 0.7f));
        TestWorld.Close(world.Rules.Units.ResistCap, world.Resist(DamageKind.Physical, 0.95f, 0f));
        TestWorld.Close(world.Rules.Units.ResistCap, world.Resist(DamageKind.Magical, 0f, 0.95f));
    }

    [Fact]
    public void ItemDamageMultiplierAppliesToItemsButNotTheBasicAttack()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1, character: "smith");
        int enemy = world.SpawnEnemy(content.EnemyIndex("duskslime"), world.Player.Pos + new Vector2(100, 0));
        int item = content.ItemIndex("sprats");

        TestWorld.Close(1.15f, world.OutgoingMultiplier(DamageKind.Physical, fromItem: true));
        TestWorld.Close(1f, world.OutgoingMultiplier(DamageKind.Physical, fromItem: false));

        float hp = world.EHp[enemy];
        world.DamageEnemy(enemy, 10f, DamageKind.Physical, new DamageSourceRef(world.Player.Id, item), 0, 0);
        TestWorld.Close(hp - 11.5f, world.EHp[enemy]); // 10 × 1.15

        hp = world.EHp[enemy];
        world.DamageEnemy(enemy, 10f, DamageKind.Physical, new DamageSourceRef(world.Player.Id), 0, 0);
        TestWorld.Close(hp - 10f, world.EHp[enemy]); // the basic attack gets no item bonus
    }

    [Fact]
    public void DamageEnemyAppliesTargetResistances()
    {
        string dir = TestContent.CreateScratchCopy();
        try
        {
            TestContent.EditJson(dir, "enemies.json", root =>
            {
                var bat = root.AsArray()[0]!;
                bat["hp"] = 100;
                bat["armor"] = 0.5;
                bat["magicResist"] = 0.25;
            });
            var content = ContentLoader.Load(dir);
            var world = new RunWorld(content, seed: 1, character: "octarine");
            int enemy = world.SpawnEnemy(content.EnemyIndex("gloombat"), world.Player.Pos + new Vector2(100, 0));
            var source = new DamageSourceRef(world.Player.Id);

            float hp = world.EHp[enemy];
            world.DamageEnemy(enemy, 10f, DamageKind.Physical, source, 0, 0);
            TestWorld.Close(hp - 5f, world.EHp[enemy]); // 10 × 1 × (1 − 0.5)

            hp = world.EHp[enemy];
            world.DamageEnemy(enemy, 10f, DamageKind.Magical, source, 0, 0);
            TestWorld.Close(hp - 7.5f, world.EHp[enemy]); // 10 × 1 × (1 − 0.25)

            hp = world.EHp[enemy];
            world.DamageEnemy(enemy, 10f, DamageKind.Pure, source, 0, 0);
            TestWorld.Close(hp - 10f, world.EHp[enemy]); // pure ignores both defences
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ContactHitUsesPlayerArmor()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1, character: "octarine");
        int enemy = world.SpawnEnemy(content.EnemyIndex("gloombat"), world.Player.Pos);
        float enemyDamage = world.EDamage[enemy];
        float hpBefore = world.Player.Hp;
        float armor = world.Player.Stats[Stat.Armor];

        world.Step(default);

        TestWorld.Close(hpBefore - enemyDamage * (1 - armor), world.Player.Hp);
        Assert.True(world.Player.Hp < hpBefore, "a contact hit must hurt the player");
    }

    [Theory]
    [InlineData("octarine", 2f)]
    [InlineData("swordsman", 2f)]
    [InlineData("smith", 2.75f)]
    [InlineData("goflin", 2f)]
    [InlineData("celestial_mage", 1f)]
    [InlineData("watchman", 5f)]
    public void CoreCharacterRegeneratesHealthUpToItsMaximum(string character, float hpPerSecond)
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 1, character: character);
        float maxHp = world.Player.Hp;
        float injuredHp = maxHp - 10f;
        world.Player.Hp = injuredHp;

        for (int tick = 0; tick < RunWorld.TicksPerSecond; tick++)
            world.Step(default);

        Assert.InRange(world.Player.Hp, injuredHp + hpPerSecond - 0.002f, injuredHp + hpPerSecond + 0.002f);

        world.Player.Hp = maxHp - hpPerSecond * RunWorld.Dt / 2f;
        world.Step(default);

        Assert.Equal(maxHp, world.Player.Hp);
    }

    [Fact]
    public void BasicAttackDealsDamageTimesMultipliers()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1, character: "octarine");
        int def = content.EnemyIndex("duskslime");
        int enemy = world.SpawnEnemy(def, world.Player.Pos + new Vector2(100, 0));
        float hp = world.EHp[enemy];
        float expected = world.Player.Stats[Stat.Damage]
                         * world.OutgoingMultiplier(DamageKind.Physical, fromItem: false)
                         * (1 - world.Resist(DamageKind.Physical, content.Enemies[def].Armor, content.Enemies[def].MagicResist));

        GameEvent hit = default;
        bool found = false;
        int ticks = 0;
        for (; ticks < 60 && !found; ticks++)
        {
            world.ClearEvents();
            world.Step(default);
            foreach (var e in world.Events)
                if (e.Kind == EventKind.Hit)
                {
                    hit = e;
                    found = true;
                    break;
                }
        }

        Assert.True(found, "the basic attack must hit an enemy in range");
        Assert.True(ticks <= RunWorld.TicksPerSecond,
            $"the first hit must land within one attack interval, took {ticks} ticks");
        TestWorld.Close(expected, hit.Value);
        TestWorld.Close(hp - expected, world.EHp[enemy]);
    }

    [Fact]
    public void WatchmanAttacksTwiceAsOftenAsOctarine()
    {
        int octarine = CountBasicBolts("octarine");
        int watchman = CountBasicBolts("watchman");

        Assert.InRange(octarine, 2, 4);
        Assert.InRange(watchman, 5, 7);
        Assert.True(watchman >= octarine * 1.8, $"watchman fired {watchman} bolts, octarine {octarine}");
    }

    [Fact]
    public void BoltCopiesFireAtDistinctTargetsAndProjectilesPierce()
    {
        var (world, enemy, item) = PrepareWeapon(new ItemWeapon
        {
            Kind = "bolt", Damage = 5, Range = 150, Speed = 240, Pierce = 2,
        });
        world.GiveItem(item);
        int front = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(35, 0));
        int behind = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(65, 0));
        int other = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(0, 45));

        world.Step(default);
        Assert.Equal(2f, Assert.Single(world.Events, e => e.Kind == EventKind.Bolt).Value);
        for (int i = 0; i < 12; i++) world.Step(default);

        foreach (int slot in new[] { front, behind, other })
            TestWorld.Close(world.EMaxHp[slot] - 5, world.EHp[slot]);
    }

    [Fact]
    public void BoltSplashDamagesNearbyEnemiesWithFalloff()
    {
        var (world, enemy, _) = PrepareWeapon(new ItemWeapon
        {
            Kind = "bolt", Damage = 20, Range = 150, Speed = 240, Pierce = 1,
        });
        int primary = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(60, 0));
        int near = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(60, 10));
        int far = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(60, 26));
        int outside = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(60, 45));

        world.Step(default);
        for (int i = 0; i < 12; i++) world.Step(default);

        // the primary target takes the direct hit only: splash must not double-dip it
        TestWorld.Close(world.EMaxHp[primary] - 20, world.EHp[primary]);
        // both enemies inside the splash radius are hit
        Assert.True(world.EHp[near] < world.EMaxHp[near]);
        Assert.True(world.EHp[far] < world.EMaxHp[far]);
        // falloff: the closer enemy loses strictly more hp than the farther one
        Assert.True(world.EMaxHp[near] - world.EHp[near] > world.EMaxHp[far] - world.EHp[far]);
        // the enemy outside the splash radius is untouched
        Assert.Equal(world.EMaxHp[outside], world.EHp[outside]);
    }

    [Fact]
    public void ChainCopiesAddHopsWithoutHittingTheSameEnemyTwice()
    {
        var (world, enemy, item) = PrepareWeapon(new ItemWeapon
        {
            Kind = "chain", Damage = 5, Range = 45, Area = 35,
        });
        world.GiveItem(item);
        world.GiveItem(item);
        int first = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(35, 0));
        int second = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(60, 0));
        int third = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(85, 0));
        int outOfReach = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(140, 0));

        world.Step(default);

        var chain = Assert.Single(world.Events, e => e.Kind == EventKind.Chain);
        Assert.Equal(4, chain.B); // four arc points, including the player
        Assert.Equal(world.EnemyPos(third, 1) - new Vector2(0, 6), world.ChainPoints[chain.A + 3]);
        foreach (int slot in new[] { first, second, third })
            TestWorld.Close(world.EMaxHp[slot] - 5, world.EHp[slot]);
        Assert.Equal(world.EMaxHp[outOfReach], world.EHp[outOfReach]);
    }

    [Fact]
    public void BlastSelectsTheDensestClusterWithinCastRange()
    {
        var (world, enemy, _) = PrepareWeapon(new ItemWeapon
        {
            Kind = "blast", Damage = 5, Range = 150, Area = 30,
        });
        int lone = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(35, 0));
        int center = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(115, 0));
        int upper = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(115, -23));
        int lower = world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(115, 23));
        for (int i = 0; i < 4; i++)
            world.SpawnEnemy(enemy, world.Player.Pos + new Vector2(-175 - i * 12, 0));

        world.Step(default);

        var blast = Assert.Single(world.Events, e => e.Kind == EventKind.Blast);
        Assert.Equal(world.EnemyPos(center, 1), new Vector2(blast.X, blast.Y));
        Assert.Equal(world.EMaxHp[lone], world.EHp[lone]);
        Assert.True(world.EHp[upper] < world.EMaxHp[upper]);
        Assert.True(world.EHp[lower] < world.EMaxHp[lower]);
    }

    private static (RunWorld World, int Enemy, int Item) PrepareWeapon(ItemWeapon weapon)
    {
        var content = TestContent.LoadCore();
        int enemy = content.EnemyIndex("gloombat");
        content.Enemies[enemy].Hp = 100;
        content.Enemies[enemy].Speed = 0;
        content.Enemies[enemy].Armor = 0;
        int item = content.Items.Count;
        content.Items.Add(new ItemDef { Id = "test_weapon", Weapon = weapon });
        var world = new RunWorld(content);
        world.Player.AttackTimer = 100;
        world.GiveItem(item);
        return (world, enemy, item);
    }

    /// <summary>Counts basic-attack bolts (item index -1) fired over three seconds at one enemy in range.</summary>
    private static int CountBasicBolts(string character)
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1, character: character);
        world.SpawnEnemy(content.EnemyIndex("duskslime"), world.Player.Pos + new Vector2(150, 0));
        int bolts = 0;
        for (int i = 0; i < 3 * RunWorld.TicksPerSecond; i++)
        {
            world.ClearEvents();
            world.Step(default);
            foreach (var e in world.Events)
                if (e.Kind == EventKind.Bolt && e.A < 0) bolts++;
        }

        return bolts;
    }
}
