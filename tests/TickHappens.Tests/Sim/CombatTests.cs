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
