using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class EnemyAttacksTests
{
    [Fact]
    public void RotCloudDealsMagicalDamageAtItsFullRateAndExpires()
    {
        var world = IsolatedWorld();
        var center = world.Player.Pos + new Vector2(46, 0);
        int rot = world.SpawnEnemy(world.Content.EnemyIndex("rot"), center);
        Kill(world, rot);
        Assert.Equal(50f, world.EnemyClouds[0].Radius);
        var replica = IsolatedWorld();
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
        Assert.Equal(world.EnemyClouds[0].Radius, replica.EnemyClouds[0].Radius);

        float health = world.Player.Hp;
        Advance(world, 4 * RunWorld.TicksPerSecond);
        TestWorld.Close(health - 60 * (1 - world.Player.Stats[Stat.MagicResist]), world.Player.Hp, 3);
        Assert.Equal(0, world.EnemyClouds.Length);
    }

    [Fact]
    public void SparkFiresFromRangeAndItsShotReplicatesBeforeHitting()
    {
        var world = IsolatedWorld();
        var pos = world.Player.Pos + new Vector2(105, 0);
        int spark = world.SpawnEnemy(world.Content.EnemyIndex("spark"), pos);
        float health = world.Player.Hp;
        world.Step(default);
        Assert.Equal(RunWorld.EnemyWindupTicks, world.EnemyWindup(spark));

        var replica = IsolatedWorld();
        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
        Assert.Equal(RunWorld.EnemyWindupTicks, replica.EnemyWindup(spark));

        Advance(world, RunWorld.EnemyWindupTicks);
        Assert.Equal(1, world.EnemyShots.Length);
        Assert.Equal(health, world.Player.Hp);
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
        Assert.Equal(1, replica.EnemyShots.Length);
        Assert.Equal(world.EnemyShots[0].Velocity, replica.EnemyShots[0].Velocity);

        Advance(world, 25);
        TestWorld.Close(health - 10 * (1 - world.Player.Stats[Stat.MagicResist]), world.Player.Hp);
        Assert.Equal(0, world.EnemyShots.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KamikazeExplodesOnProximityOrDeathAndDamagesBothSides(bool killedByPlayer)
    {
        var world = IsolatedWorld();
        int kamikaze = world.Content.EnemyIndex("kamikaze");
        var pos = world.Player.Pos;
        int bomber = world.SpawnEnemy(kamikaze, pos + new Vector2(20, 0));
        int nearby = world.SpawnEnemy(world.Content.EnemyIndex("rot"), pos + new Vector2(55, 0));
        int distant = world.SpawnEnemy(world.Content.EnemyIndex("rot"), pos + new Vector2(150, 0));
        if (killedByPlayer) Kill(world, bomber);

        Advance(world, RunWorld.EnemyWindupTicks + 1);
        Assert.False(world.EAlive[bomber]);
        Assert.True(world.EHp[nearby] <= 30);
        Assert.Equal(80f, world.EHp[distant]);
        var hurt = Assert.Single(world.Events, e => e.Kind == EventKind.PlayerHurt);
        TestWorld.Close(50 * (1 - world.Player.Stats[Stat.MagicResist]), hurt.Value);
        Assert.Contains(world.Events, e => e.Kind == EventKind.EnemyBlast);
    }

    [Fact]
    public void ExistingEnemyMovesOnePercentFasterAfterOneMinute()
    {
        var world = IsolatedWorld();
        var start = world.Player.Pos + new Vector2(300, 0);
        int enemy = world.SpawnEnemy(world.Content.EnemyIndex("gloombat"), start);
        world.Step(default);
        float firstStep = start.X - world.EX[enemy];

        const int minuteTicks = 60 * RunWorld.TicksPerSecond;
        for (int i = 0; i < minuteTicks; i++)
        {
            world.EX[enemy] = start.X;
            world.Step(default);
        }

        Assert.True(world.EAlive[enemy]);
        Assert.True(firstStep > 0);
        TestWorld.Close(firstStep * 1.01f, start.X - world.EX[enemy], 3);
    }

    private static RunWorld IsolatedWorld()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Characters[0].Stats.Regen = 0;
        var world = new RunWorld(content);
        world.Player.AttackTimer = 100;
        return world;
    }

    private static void Advance(RunWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.Step(default);
    }

    private static void Kill(RunWorld world, int slot) =>
        world.DamageEnemy(slot, 1000, DamageKind.Pure, new DamageSourceRef(0), world.EX[slot], world.EY[slot]);
}
