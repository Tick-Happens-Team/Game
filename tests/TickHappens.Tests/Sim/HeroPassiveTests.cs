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
    public void BoarChargesAfterFiveSecondsAndRamsOnlyOncePerContact()
    {
        var world = new RunWorld(QuietContent(), character: "pig");
        TestWorld.DisableCrits(world);
        var player = world.Player;
        player.AttackTimer = 100;
        var forward = new RunInput(1, 0);
        for (int i = 0; i < 5 * RunWorld.TicksPerSecond; i++) world.Step(forward);

        var replica = new RunWorld(world.Content, character: "pig");
        CopySnapshot(world, replica);
        Assert.Equal(5 * RunWorld.TicksPerSecond, replica.Player.BoarWalkTicks);

        int target = world.SpawnEnemy(0, player.Pos + new Vector2(10, 0));
        float hp = world.EHp[target];
        var start = player.Pos;
        world.Step(forward);
        TestWorld.Close(1.25f * 38 * world.Rules.Units.MoveSpeedScale * RunWorld.Dt, player.Pos.X - start.X);
        TestWorld.Close(hp - 0.055f * world.MaxHealth, world.EHp[target]);
        Assert.True(world.EKx[target] > 0);

        world.Step(default);
        TestWorld.Close(hp - 0.055f * world.MaxHealth, world.EHp[target]);
        Assert.Equal(0, player.BoarWalkTicks);
    }

    [Fact]
    public void MinerAccumulatesDamageAndExplodesAroundThePlayer()
    {
        var world = new RunWorld(QuietContent(enemyDamage: 60), character: "miner");
        int target = world.SpawnEnemy(0, world.Player.Pos + new Vector2(30, 0));
        world.SpawnEnemy(0, world.Player.Pos);
        world.Step(default);
        Assert.DoesNotContain(world.Events, e => e.Kind == EventKind.Blast);
        Assert.InRange(world.Player.PendingBlastDamage, 1, 79.99f);
        for (int i = 0; i < 2 * RunWorld.TicksPerSecond; i++)
        {
            world.ClearEvents();
            world.Step(default);
            if (world.Events.Any(e => e.Kind == EventKind.Blast)) break;
        }

        var blast = Assert.Single(world.Events, e => e.Kind == EventKind.Blast);
        Assert.Equal(50, blast.Value);
        Assert.True(world.EHp[target] < world.EMaxHp[target]);
        Assert.InRange(world.Player.PendingBlastDamage, 0, 79.99f);
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
        Assert.Equal(0, world.Player.Illusions[0].UntilTick);

        world.SpawnEnemy(0, world.Player.Pos + new Vector2(-100, 0));
        world.Step(default);
        Assert.Equal(world.Tick + 8 * RunWorld.TicksPerSecond, world.Player.Illusions[0].UntilTick);
        TestWorld.Close(world.MaxHealth * 0.5f, world.Player.Illusions[0].Health);
        world.Player.Illusions[0].Health *= 0.5f;
        int mask = world.Content.ItemIndex("phantom_mask");
        world.GiveItem(mask);
        for (int i = 0; i < 9; i++) world.GiveItem(mask); // Force the proc without depending on the seed.
        var killPosition = world.Player.Pos + new Vector2(72, 0);
        int victim = world.SpawnEnemy(0, killPosition);
        world.DamageEnemy(victim, 10_000, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        Assert.Equal(world.Tick + 4 * RunWorld.TicksPerSecond, world.Player.MaskIllusionUntilTick);
        Assert.Equal(killPosition, world.Player.MaskIllusionPosition);
        var replica = new RunWorld(world.Content, character: "illusionist");
        CopySnapshot(world, replica);
        Assert.Equal(world.Player.Illusions[0].Health, replica.Player.Illusions[0].Health);
        Assert.Equal(world.Player.Illusions[0].MaxHealth, replica.Player.Illusions[0].MaxHealth);
        Assert.Equal(world.Player.MaskIllusionUntilTick, replica.Player.MaskIllusionUntilTick);
        Assert.InRange(Vector2.Distance(world.Player.MaskIllusionPosition, replica.Player.MaskIllusionPosition), 0, 0.025f);
        Assert.Contains(RunWorld.ProjectileStyle.Phantom, world.PStyle);
        Step(world, 8 * RunWorld.TicksPerSecond);
        Assert.Equal(world.Tick, world.Player.Illusions[0].UntilTick);
    }

    [Fact]
    public void IllusionsOverlapAfterLifetimeGrowthAndBothReplicate()
    {
        var world = new RunWorld(QuietContent(), character: "illusionist");
        world.Player.Level = 51;
        int interval = 15 * RunWorld.TicksPerSecond;
        Step(world, interval * 2);
        var first = world.Player.Illusions[0];
        var second = world.Player.Illusions[1];
        Assert.Equal(interval + 465, first.UntilTick);
        Assert.Equal(interval * 2 + 465, second.UntilTick);

        var replica = new RunWorld(world.Content, character: "illusionist");
        CopySnapshot(world, replica);
        Assert.Equal(first.UntilTick, replica.Player.Illusions[0].UntilTick);
        Assert.Equal(second.UntilTick, replica.Player.Illusions[1].UntilTick);
        Assert.Equal(second.Health, replica.Player.Illusions[1].Health);

        Step(world, first.UntilTick - world.Tick);
        CopySnapshot(world, replica);
        Assert.Equal(0, replica.Player.Illusions[0].UntilTick);
        Assert.Equal(second.UntilTick, replica.Player.Illusions[1].UntilTick);
    }

    [Fact]
    public void CubeGetsTheSameFirstRollForTheSameSeedAndClientReceivesTheBonuses()
    {
        var world = new RunWorld(QuietContent(), seed: 7, character: "cybernetic_cube");
        var second = new RunWorld(world.Content, seed: 7, character: "cybernetic_cube");
        TestWorld.LevelTo(world, 2);
        TestWorld.LevelTo(second, 2);

        Assert.InRange(Assert.Single(world.Player.CubeBonuses, bonus => bonus > 0), 0.0075f, 0.0825f);
        Assert.Equal(world.Player.CubeBonuses, second.Player.CubeBonuses);
        var replica = new RunWorld(world.Content, character: "cybernetic_cube");
        CopySnapshot(world, replica);
        Assert.Equal(world.Player.CubeBonuses, replica.Player.CubeBonuses);
        TestWorld.Close(world.Player.Stats[Stat.Damage], replica.Player.Stats[Stat.Damage]);
    }

    [Fact]
    public void CubeStartsWithTenDamageAndGainsThreeQuartersOfADamagePerLevelEarnedThroughXp()
    {
        var world = new RunWorld(QuietContent(), character: "cybernetic_cube");
        Step(world, 15 * RunWorld.TicksPerSecond);
        Assert.Equal(1, world.Player.Level);
        Assert.Equal(10, world.Player.Stats.Base(Stat.Damage));

        TestWorld.LevelTo(world, 3);
        Assert.Equal(11.5f, world.Player.Stats.Base(Stat.Damage));
    }

    [Fact]
    public void MasochistGrowthAddsFlatRegenAndMovementPercentagePointsAtLowHealth()
    {
        var world = new RunWorld(QuietContent(), character: "masochist");
        TestWorld.LevelTo(world, 3);
        world.Player.Hp = world.MaxHealth * 0.5f;
        world.Step(default);

        TestWorld.Close(0.525f, world.Player.Stats[Stat.Regen]);
        TestWorld.Close(33.066f, world.Player.Stats[Stat.MoveSpeed]);
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
        Assert.InRange(wounded.Player.Stats[Stat.MoveSpeed], 32.9f, 33f);
        Assert.InRange(wounded.Player.Stats[Stat.Regen], 0.37f, 0.375f);

        var vampire = new RunWorld(content, character: "vampire");
        vampire.Player.Hp = 100;
        int enemy = vampire.SpawnEnemy(0, vampire.Player.Pos + new Vector2(30, 0));
        vampire.DamageEnemy(enemy, 10, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        TestWorld.Close(100.35f, vampire.Player.Hp);

        TestWorld.LevelTo(vampire, 2);
        vampire.Player.Hp = 100;
        enemy = vampire.SpawnEnemy(0, vampire.Player.Pos + new Vector2(30, 0));
        vampire.DamageEnemy(enemy, 10, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        TestWorld.Close(100.375f, vampire.Player.Hp);
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
