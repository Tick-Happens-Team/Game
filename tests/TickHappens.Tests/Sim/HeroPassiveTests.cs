using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class HeroPassiveTests
{
    [Fact]
    public void PigStaysActiveAsAGhostUntilTheTimerExpires()
    {
        var world = new RunWorld(QuietContent(enemyDamage: 500), character: "pig");
        var player = world.Player;
        world.SpawnEnemy(0, player.Pos);
        world.Step(default);

        Assert.Equal(world.Tick + 5 * RunWorld.TicksPerSecond, player.GhostUntilTick);
        Assert.Equal(1, player.Hp);
        var replica = new RunWorld(world.Content, character: "pig");
        CopySnapshot(world, replica);
        Assert.Equal(player.GhostUntilTick, replica.Player.GhostUntilTick);

        Step(world, 5 * RunWorld.TicksPerSecond - 1);
        Assert.Equal(RunPhase.Playing, world.Phase);
        Assert.Equal(1, player.Hp);
        world.Step(default);
        Assert.Equal(RunPhase.Dead, world.Phase);
    }

    [Fact]
    public void MinerAccumulatesDamageAndExplodesAroundThePlayer()
    {
        var world = new RunWorld(QuietContent(enemyDamage: 60), character: "miner");
        int target = world.SpawnEnemy(0, world.Player.Pos + new Vector2(30, 0));
        world.SpawnEnemy(0, world.Player.Pos);
        world.Step(default);

        var blast = Assert.Single(world.Events, e => e.Kind == EventKind.Blast);
        Assert.Equal(50, blast.Value);
        Assert.True(world.EHp[target] < world.EMaxHp[target]);
        Assert.InRange(world.Player.PendingBlastDamage, 0, 49.99f);
    }

    [Theory]
    [InlineData(20, -1)]
    [InlineData(21, -2)]
    [InlineData(50, -2)]
    [InlineData(51, -3)]
    public void NecromancerSpendsSoulsOnTheCorrectCompanionAndReplicatesIt(int souls, int type)
    {
        var world = new RunWorld(QuietContent(), character: "necromancer");
        var replica = new RunWorld(world.Content, character: "necromancer");
        for (int i = 0; i < souls; i++)
        {
            int target = world.SpawnEnemy(0, world.Player.Pos + new Vector2(30, 0));
            world.DamageEnemy(target, 10_000, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        }
        Assert.Equal(souls, world.Player.Souls);
        TestWorld.Close(1 + souls * 0.005f, world.OutgoingMultiplier(DamageKind.Pure, false));
        CopySnapshot(world, replica);
        Assert.Equal(souls, replica.Player.Souls);

        Step(world, 30 * RunWorld.TicksPerSecond);
        var companion = Assert.Single(world.Companions.ToArray(), ally => ally.Active);
        Assert.Equal(type, companion.Item);
        Assert.Equal(souls, companion.MaxHealth);
        Assert.Equal(0, world.Player.Souls);
        TestWorld.Close(1, world.OutgoingMultiplier(DamageKind.Pure, false));

        CopySnapshot(world, replica);
        Assert.Equal(0, replica.Player.Souls);
        var replicaCompanion = Assert.Single(replica.Companions.ToArray(), ally => ally.Active);
        Assert.Equal(type, replicaCompanion.Item);
        Assert.Equal(companion.Health, replicaCompanion.Health);
        Assert.Equal(companion.AuraCapacity, replicaCompanion.AuraCapacity);
    }

    [Fact]
    public void IllusionSpawnsOnScheduleRepeatsTheAttackAndExpires()
    {
        var world = new RunWorld(QuietContent(), character: "illusionist");
        int interval = 15 * RunWorld.TicksPerSecond;
        Step(world, interval - 1);
        Assert.Equal(0, world.Player.IllusionUntilTick);

        world.SpawnEnemy(0, world.Player.Pos + new Vector2(-100, 0));
        world.Step(default);
        Assert.Equal(world.Tick + 8 * RunWorld.TicksPerSecond, world.Player.IllusionUntilTick);
        TestWorld.Close(world.MaxHealth * 0.3f, world.Player.IllusionHealth);
        world.Player.IllusionHealth *= 0.5f;
        var replica = new RunWorld(world.Content, character: "illusionist");
        CopySnapshot(world, replica);
        Assert.Equal(world.Player.IllusionHealth, replica.Player.IllusionHealth);
        Assert.Equal(world.Player.IllusionMaxHealth, replica.Player.IllusionMaxHealth);
        Assert.Contains(RunWorld.ProjectileStyle.Phantom, world.PStyle);
        Step(world, 8 * RunWorld.TicksPerSecond);
        Assert.Equal(world.Tick, world.Player.IllusionUntilTick);
    }

    [Fact]
    public void CubeGetsTheSameFirstRollForTheSameSeedAndClientReceivesTheBonuses()
    {
        var world = new RunWorld(QuietContent(), seed: 7, character: "cybernetic_cube");
        var second = new RunWorld(world.Content, seed: 7, character: "cybernetic_cube");
        TestWorld.LevelTo(world, 2);
        TestWorld.LevelTo(second, 2);

        Assert.InRange(Assert.Single(world.Player.CubeBonuses, bonus => bonus > 0), 0.01f, 0.05f);
        Assert.Equal(world.Player.CubeBonuses, second.Player.CubeBonuses);
        var replica = new RunWorld(world.Content, character: "cybernetic_cube");
        CopySnapshot(world, replica);
        Assert.Equal(world.Player.CubeBonuses, replica.Player.CubeBonuses);
        TestWorld.Close(world.Player.Stats[Stat.Damage], replica.Player.Stats[Stat.Damage]);
    }

    [Fact]
    public void CubeStartsWithEightDamageAndGainsHalfADamagePerLevelEarnedThroughXp()
    {
        var world = new RunWorld(QuietContent(), character: "cybernetic_cube");
        Step(world, 15 * RunWorld.TicksPerSecond);
        Assert.Equal(1, world.Player.Level);
        Assert.Equal(8, world.Player.Stats.Base(Stat.Damage));

        TestWorld.LevelTo(world, 3);
        Assert.Equal(9, world.Player.Stats.Base(Stat.Damage));
    }

    [Fact]
    public void DiverMovementIsLockedAndDistanceAddsArmor()
    {
        var world = new RunWorld(QuietContent(), character: "diver");
        world.Player.Stats.Apply(Stat.MoveSpeed, 1000, 0);
        var start = world.Player.Pos;
        for (int i = 0; i < 4 * RunWorld.TicksPerSecond; i++)
            world.Step(new RunInput(1, 0, false));
        Assert.Equal(120, world.Player.Pos.X - start.X, 2);
        TestWorld.Close(0.08f, world.Player.Stats[Stat.Armor]);
        TestWorld.LevelTo(world, 2);
        TestWorld.Close(0.0025f, world.Player.Stats[Stat.DamageReflection]);
    }

    [Fact]
    public void PuppeteerBuffsOtherPlayersAndSummons()
    {
        var world = new RunWorld(QuietContent(), character: "puppeteer", playerCount: 2);
        world.SetPlayerActive(1, true);
        world.SetStartingHero(1, world.Content.CharacterIndex("octarine"));
        world.GiveItem(world.Content.ItemIndex("sausages"));
        world.StepCoop([default, default]);
        TestWorld.Close(20, world.Player.Stats[Stat.AttackSpeed]);
        TestWorld.Close(27.5f, world.Players[1].Stats[Stat.AttackSpeed]);
        TestWorld.Close(0.1f, world.Players[1].PuppeteerDamageBonus);
        var companion = Assert.Single(world.Companions.ToArray(), ally => ally.Active);
        TestWorld.Close(0.1f, companion.DamageBonus);
        TestWorld.Close(0.1f, companion.AttackSpeedBonus);
    }

    [Fact]
    public void SkaterTrailBurnsEnemiesAfterTheSkaterMovesPastThem()
    {
        var world = new RunWorld(QuietContent(), character: "skater");
        world.Player.Stats.SetBase(Stat.Damage, 0);
        for (int i = 0; i < 6; i++) world.Step(new RunInput(1, 0, false));
        int enemy = world.SpawnEnemy(0, world.Player.Prev);
        Step(world, 6);
        Assert.Contains(world.Events, e => e.Kind == EventKind.HeroFireTrail);
        Assert.True(world.EHp[enemy] < world.EMaxHp[enemy]);
    }

    [Fact]
    public void MissingHealthBoostsMobilityAndRegenerationAndVampireHealsFromDamageDealt()
    {
        var content = QuietContent();
        var wounded = new RunWorld(content, character: "masochist");
        TestWorld.Close(wounded.Player.Character.Stats.Damage, wounded.Player.Stats[Stat.Damage]);
        wounded.Player.Hp *= 0.5f;
        wounded.Step(default);
        TestWorld.Close(wounded.Player.Character.Stats.Damage, wounded.Player.Stats[Stat.Damage]);
        TestWorld.Close(wounded.Player.Character.Stats.AttackSpeed, wounded.Player.Stats[Stat.AttackSpeed]);
        Assert.InRange(wounded.Player.Stats[Stat.MoveSpeed], 29.9f, 30f);
        Assert.InRange(wounded.Player.Stats[Stat.Regen], 1.49f, 1.5f);

        var vampire = new RunWorld(content, character: "vampire");
        vampire.Player.Hp = 100;
        int enemy = vampire.SpawnEnemy(0, vampire.Player.Pos + new Vector2(30, 0));
        vampire.DamageEnemy(enemy, 10, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        TestWorld.Close(101, vampire.Player.Hp);
    }

    private static void Step(RunWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.Step(default);
    }

    private static void CopySnapshot(RunWorld host, RunWorld client)
    {
        var bytes = new byte[RunWorld.SnapshotCapacity];
        int size = host.WriteSnapshot(bytes);
        Assert.True(client.TryApplySnapshot(bytes.AsSpan(0, size)));
    }

    private static ContentSet QuietContent(float enemyDamage = 0)
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        content.Enemies[0].Damage = enemyDamage;
        content.Enemies[0].Hp = 1000;
        content.Enemies[0].Speed = 0;
        return content;
    }
}
