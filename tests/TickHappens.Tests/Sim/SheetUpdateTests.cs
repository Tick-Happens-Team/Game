using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class SheetUpdateTests
{
    private static RunWorld World(string hero = "swordsman")
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        content.Waves.BossIntervalMinutes = 0;
        content.Enemies[0].Hp = 1000;
        content.Enemies[0].Damage = 10;
        content.Enemies[0].Speed = 0;
        content.Enemies[0].DamageType = "pure";
        var world = new RunWorld(content, seed: 19, character: hero);
        TestWorld.DisableCrits(world);
        world.Player.AttackTimer = 1000;
        return world;
    }

    private static void Step(RunWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++) { world.ClearEvents(); world.Step(default); }
    }

    [Fact]
    public void CharmSelectsAnOrdinaryEnemyAttacksForSeventyPercentReplicatesAndExpires()
    {
        var world = World("succubus");
        int boss = world.SpawnEnemy(world.Content.EnemyIndex("devourer"), world.Player.Pos + new Vector2(35, 0));
        int elite = world.SpawnEnemy(world.Content.EnemyIndex("butcher"), world.Player.Pos + new Vector2(55, 0));
        int charmed = world.SpawnEnemy(0, world.Player.Pos + new Vector2(100, 0));
        int victim = world.SpawnEnemy(0, world.Player.Pos + new Vector2(112, 0));
        world.Player.InvulnTicks = 1000;
        world.Player.NextHeroPassiveTick = 1;
        Step(world, 1);
        Assert.True(world.IsCharmed(charmed));
        Assert.False(world.IsCharmed(boss));
        Assert.False(world.IsCharmed(elite));
        Step(world, 1);
        TestWorld.Close(993, world.EHp[victim]);

        var replica = new RunWorld(world.Content, character: "succubus");
        var snapshot = new byte[RunWorld.SnapshotCapacity];
        int size = world.WriteSnapshot(snapshot);
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, size)));
        Assert.True(replica.IsCharmed(charmed));
        Step(world, 90);
        Assert.False(world.IsCharmed(charmed));
    }

    [Fact]
    public void AttackShredStacksByCopyCandleExtendsItAndDevilSwordDoesNotStack()
    {
        var world = World();
        var p = world.Player;
        world.Content.Enemies[0].Armor = 0.5f;
        foreach (string id in new[] { "file", "file", "black_candle", "black_candle", "devil_sword", "devil_sword" })
            world.GiveItem(world.Content.ItemIndex(id));
        int enemy = world.SpawnEnemy(0, p.Pos + new Vector2(100, 0));
        world.DamageEnemy(enemy, 10, DamageKind.Physical, new(0), p.Pos.X, p.Pos.Y, 0);
        TestWorld.Close(995, world.EHp[enemy]);
        TestWorld.Close(0.16f, world.ArmorShredAt(enemy));
        world.DamageEnemy(enemy, 10, DamageKind.Physical, new(0), p.Pos.X, p.Pos.Y, 0);
        TestWorld.Close(995 - 10 * 1.22f * 0.66f, world.EHp[enemy]);
        Step(world, 91);
        TestWorld.Close(0.16f, world.ArmorShredAt(enemy));
        Step(world, 14);
        TestWorld.Close(0, world.ArmorShredAt(enemy));
    }

    [Fact]
    public void SnowballSlowsByFourPercentPerCopy()
    {
        var normal = World();
        var slowed = World();
        normal.Content.Enemies[0].Speed = slowed.Content.Enemies[0].Speed = 10;
        slowed.GiveItem(slowed.Content.ItemIndex("snowball"));
        slowed.GiveItem(slowed.Content.ItemIndex("snowball"));
        int a = normal.SpawnEnemy(0, normal.Player.Pos + new Vector2(100, 0));
        int b = slowed.SpawnEnemy(0, slowed.Player.Pos + new Vector2(100, 0));
        normal.DamageEnemy(a, 1, DamageKind.Pure, new(0), 0, 0, 0);
        slowed.DamageEnemy(b, 1, DamageKind.Pure, new(0), 0, 0, 0);
        float start = normal.EX[a];
        Step(normal, 1);
        Step(slowed, 1);
        TestWorld.Close((start - normal.EX[a]) * 0.92f, start - slowed.EX[b]);
    }

    [Fact]
    public void VacuumPullsOnlyEnemiesInRangeAndStunsForTwoSeconds()
    {
        var world = World();
        world.GiveItem(world.Content.ItemIndex("vacuum"));
        int near = world.SpawnEnemy(0, world.Player.Pos + new Vector2(200, 0));
        int far = world.SpawnEnemy(0, world.Player.Pos + new Vector2(310, 0));
        float farX = world.EX[far];
        float hp = world.Player.Hp;
        Step(world, 1);
        Assert.InRange(world.EX[near] - world.Player.Pos.X, 10, 12);
        Assert.Equal(farX, world.EX[far]);
        Step(world, 58);
        Assert.True(world.Player.Hp >= hp);
        Assert.Equal(1000, world.EHp[near]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PillEnlargesProjectileAndSplashAndReplicatesTheSize(bool pill)
    {
        var world = World();
        if (pill) world.GiveItem(world.Content.ItemIndex("pill"));
        world.GiveItem(world.Content.ItemIndex("file"));
        int target = world.SpawnEnemy(0, world.Player.Pos + new Vector2(60, 0));
        int nearby = world.SpawnEnemy(0, world.Player.Pos + new Vector2(60, 45));
        world.Player.AttackTimer = 0;
        Step(world, 1);
        int shot = Array.FindIndex(world.PAlive, active => active);
        Assert.True(shot >= 0);
        Assert.Equal(pill ? 2 : 1, world.PScale[shot]);
        var replica = new RunWorld(world.Content);
        var bytes = new byte[RunWorld.SnapshotCapacity];
        int size = world.WriteSnapshot(bytes);
        Assert.True(replica.TryApplySnapshot(bytes.AsSpan(0, size)));
        Assert.Equal(world.PScale[shot], replica.PScale[0]);
        Step(world, 15);
        Assert.True(world.EHp[target] < 1000);
        Assert.Equal(pill, world.EHp[nearby] < 1000);
        TestWorld.Close(pill ? 0.08f : 0, world.ArmorShredAt(nearby));
    }

    [Fact]
    public void NewContentRemainsDeterministic()
    {
        var a = World("succubus");
        var b = World("succubus");
        foreach (var w in new[] { a, b })
        {
            foreach (string id in new[] { "vacuum", "file", "snowball", "black_candle", "devil_sword", "pill" })
                w.GiveItem(w.Content.ItemIndex(id));
            for (int i = 0; i < 20; i++) w.SpawnEnemy(0, w.Player.Pos + new Vector2(30 + i * 8, 15));
            w.Player.AttackTimer = 0;
        }
        Step(a, 1000);
        Step(b, 1000);
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
