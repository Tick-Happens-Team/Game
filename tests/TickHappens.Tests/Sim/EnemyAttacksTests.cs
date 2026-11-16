using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class EnemyAttacksTests
{
    [Fact]
    public void RotCloudDealsMagicalDamageAtItsFullRateAndExpires()
    {
        var world = IsolatedWorld();
        var center = world.Player.Pos + new Vector2(46, 0);
        int rotDef = world.Content.EnemyIndex("rot");
        world.Content.Enemies[rotDef].Xp = 0;
        int rot = world.SpawnEnemy(rotDef, center);
        Kill(world, rot);
        Assert.Equal(200f / 3, world.EnemyClouds[0].Radius);
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

    [Fact]
    public void WitchLightningHitsThePrimaryTargetBeforeChainingToNearbyPlayers()
    {
        var world = IsolatedWorld(playerCount: 2);
        world.SetPlayerActive(1, true);
        var primary = world.Players[1];
        var secondary = world.Players[0];
        primary.Pos = secondary.Pos + new Vector2(80, 0);
        primary.AttackTimer = 100;
        int witchType = world.Content.EnemyIndex("witch");
        world.Content.Enemies[witchType].Speed = 0;
        var witchPos = primary.Pos + new Vector2(180, 0);
        world.SpawnEnemy(witchType, witchPos);

        for (int i = 0; i <= RunWorld.EnemyWindupTicks; i++)
            world.StepCoop([default, default]);

        var beams = world.Events.Where(e => e.Kind == EventKind.ItemBeam).ToArray();
        Assert.Equal(2, beams.Length);
        Assert.Equal(witchPos.X, beams[0].X);
        Assert.Equal(primary.Pos.X, beams[0].X2);
        Assert.Equal(primary.Pos.X, beams[1].X);
        Assert.Equal(secondary.Pos.X, beams[1].X2);
        Assert.Equal(2, world.Events.Count(e => e.Kind == EventKind.PlayerHurt));
        world.ClearEvents();
        for (int i = 0; i < 5 * RunWorld.TicksPerSecond - 1; i++)
            world.StepCoop([default, default]);
        Assert.DoesNotContain(world.Events, e => e.Kind == EventKind.ItemBeam);
        world.StepCoop([default, default]);
        Assert.Equal(2, world.Events.Count(e => e.Kind == EventKind.ItemBeam));
    }

    [Fact]
    public void JumperQuakeIsCenteredOnItsLandingAndReplicates()
    {
        var world = IsolatedWorld();
        var start = world.Player.Pos + new Vector2(86, 0);
        int slot = world.SpawnEnemy(world.Content.EnemyIndex("jumper"), start);
        Advance(world, 10);
        Assert.DoesNotContain(world.Events, e => e.Kind == EventKind.EnemyQuake || e.Kind == EventKind.PlayerHurt);

        world.Step(default);
        var quake = Assert.Single(world.Events, e => e.Kind == EventKind.EnemyQuake);
        Assert.Equal(new Vector2(world.EX[slot], world.EY[slot]), new Vector2(quake.X, quake.Y));
        Assert.Equal(50, quake.Value);
        Assert.Contains(world.Events, e => e.Kind == EventKind.PlayerHurt);
        Assert.DoesNotContain(world.Events, e => e.Kind == EventKind.EnemyBlast);
        Span<byte> packet = stackalloc byte[CoopSession.MaxEventBytes];
        int next = 0;
        int bytes = world.WriteEvents(packet, ref next);
        var replica = IsolatedWorld();
        Assert.True(replica.TryApplyEvents(packet[..bytes]));
        Assert.Contains(replica.Events, e => e.Kind == EventKind.EnemyQuake);
    }

    [Theory]
    [InlineData("gloombat")]
    [InlineData("amberslime")]
    public void ContactEnemyWaitsOneSecondBetweenHits(string enemyId)
    {
        var world = IsolatedWorld();
        world.SpawnEnemy(world.Content.EnemyIndex(enemyId), world.Player.Pos);

        Advance(world, RunWorld.TicksPerSecond);
        Assert.Single(world.Events, e => e.Kind == EventKind.PlayerHurt);
        world.Step(default);
        Assert.Equal(2, world.Events.Count(e => e.Kind == EventKind.PlayerHurt));
    }

    [Fact]
    public void DashPassesThroughEnemiesAndEnemiesCanOverlap()
    {
        var world = IsolatedWorld();
        var player = world.Player;
        player.Facing = Vector2.UnitX;
        int def = world.Content.EnemyIndex("gloombat");
        float enemyRadius = world.Content.Enemies[def].Radius;
        world.Content.Enemies[def].Speed = 0;
        var start = player.Pos;
        int front = world.SpawnEnemy(def, start + new Vector2(22, 0));
        int rear = world.SpawnEnemy(def, start + new Vector2(22 + enemyRadius, 0));

        world.Step(new RunInput(1, 0, Dash: true));
        Advance(world, 8);

        Assert.True(player.Pos.X > world.EX[rear] + RunWorld.PlayerRadius + enemyRadius);
        TestWorld.Close(enemyRadius, world.EX[rear] - world.EX[front]);
    }

    [Fact]
    public void CoopTeammatesBlockMovementOnBothHostAndReplica()
    {
        var host = IsolatedWorld(playerCount: 2);
        host.SetPlayerActive(1, true);
        host.Players[1].Pos = host.Player.Pos + new Vector2(14, 0);
        var replica = IsolatedWorld(playerCount: 2);
        Span<byte> packet = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(packet[..host.WriteSnapshot(packet)]));
        var input = new RunInput(1, 0);
        host.StepCoop([input, default]);
        replica.PredictLocalMovement(input);
        Assert.InRange(Vector2.Distance(host.Player.Pos, replica.Player.Pos), 0, 0.03f);
        TestWorld.Close(2 * RunWorld.PlayerRadius, host.Players[1].Pos.X - host.Player.Pos.X);
        Assert.InRange(Vector2.Distance(replica.Players[1].Pos, replica.Player.Pos), 11.99f, 12.01f);
    }

    [Fact]
    public void TimekeeperAccelerationIsVisibleOnReplicasUntilTheBossDies()
    {
        var host = IsolatedWorld();
        var replica = IsolatedWorld();
        int boss = host.SpawnEnemy(host.Content.EnemyIndex("timekeeper"), host.Player.Pos + new Vector2(400, 0));
        host.Step(default);
        Span<byte> packet = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(packet[..host.WriteSnapshot(packet)]));
        Assert.True(replica.TimeAccelerated);
        TestWorld.Close(2 * RunWorld.Dt, replica.RunSeconds);

        host.DamageEnemy(boss, 10_000, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        host.Step(default);
        Assert.True(replica.TryApplySnapshot(packet[..host.WriteSnapshot(packet)]));
        Assert.False(replica.TimeAccelerated);
        TestWorld.Close(3 * RunWorld.Dt, replica.RunSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KamikazeExplodesOnProximityOrDeathAndDamagesBothSides(bool killedByPlayer)
    {
        var world = IsolatedWorld();
        int kamikaze = world.Content.EnemyIndex("kamikaze");
        int rot = world.Content.EnemyIndex("rot");
        world.Content.Enemies[kamikaze].Speed = 0;
        world.Content.Enemies[rot].Speed = 0;
        var pos = world.Player.Pos;
        int bomber = world.SpawnEnemy(kamikaze, pos + new Vector2(20, 0));
        int nearby = world.SpawnEnemy(rot, pos + new Vector2(43, 0));
        int distant = world.SpawnEnemy(rot, pos + new Vector2(74, 0));
        if (killedByPlayer)
        {
            Kill(world, bomber);
        }
        else
        {
            world.Step(default);
            int fuse = RunWorld.TicksPerSecond;
            Assert.Equal(fuse, world.EnemyWindup(bomber));
            var replica = IsolatedWorld();
            byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
            Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
            Assert.Equal(fuse, replica.EnemyWindup(bomber));
            Advance(world, fuse - 1);
            Assert.True(world.EAlive[bomber]);
        }
        world.Step(default);
        Assert.False(world.EAlive[bomber]);
        Assert.True(world.EHp[nearby] <= 30);
        Assert.Equal(80f, world.EHp[distant]);
        var hurt = Assert.Single(world.Events, e => e.Kind == EventKind.PlayerHurt);
        TestWorld.Close(50 * (1 - world.Player.Stats[Stat.MagicResist]), hurt.Value);
        var blast = Assert.Single(world.Events, e => e.Kind == EventKind.EnemyBlast);
        TestWorld.Close(55f / 1.5f, blast.Value);
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

    private static RunWorld IsolatedWorld(int playerCount = 1)
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Characters[0].Stats.Regen = 0;
        var world = new RunWorld(content, playerCount: playerCount);
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
