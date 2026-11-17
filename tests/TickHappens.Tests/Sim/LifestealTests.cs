using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;


namespace TickHappens.Tests;

public sealed class LifestealTests
{
    [Fact]
    public void LifestealHealsInFullUpToTheBudgetAndAtTwentyPercentBeyondIt()
    {
        var world = new RunWorld(QuietContent(), character: "pig");
        TestWorld.DisableCrits(world);
        var player = world.Player;
        player.Stats.Apply(Stat.Lifesteal, 0.1f, 0);
        float maxHealth = RunWorld.MaxHealthFor(player);
        float budget = 0.06f * maxHealth * 2;
        player.Hp = 1;

        int first = world.SpawnEnemy(0, player.Pos + new Vector2(60, 0));
        int second = world.SpawnEnemy(0, player.Pos + new Vector2(-60, 0));
        world.DamageEnemy(first, 100, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        TestWorld.Close(1 + 10, player.Hp, 3);
        world.DamageEnemy(second, 500, DamageKind.Pure, new DamageSourceRef(0), 0, 0);
        float overflow = 60 - budget;
        TestWorld.Close(1 + budget + overflow * 0.2f, player.Hp, 3);
        TestWorld.Close(0, world.LifestealBudget(player));

        // Auto-attacks on the remaining enemies would spend the budget while it refills.
        player.Stats.Apply(Stat.Lifesteal, -0.1f, 0);
        for (int i = 0; i < RunWorld.TicksPerSecond / 2; i++) world.Step(default);
        TestWorld.Close(0.06f * maxHealth * 0.5f, world.LifestealBudget(player), 2);
        for (int i = 0; i < 3 * RunWorld.TicksPerSecond; i++) world.Step(default);
        TestWorld.Close(budget, world.LifestealBudget(player), 3);
    }

    [Fact]
    public void VampChanHasAHigherLifestealHealingCap()
    {
        var world = new RunWorld(QuietContent(), character: "vampire");
        world.Step(default);
        TestWorld.Close(0.10f, world.Player.Stats[Stat.LifestealHealCap]);
    }

    [Fact]
    public void VampireHoodCopiesWidenTheRadiusInsteadOfAddingLifesteal()
    {
        var content = QuietContent();
        int hood = content.ItemIndex("vampire_hood");
        var world = new RunWorld(content, playerCount: 2);
        world.SetPlayerActive(1, true);
        var (owner, ally) = (world.Players[0], world.Players[1]);
        ally.Pos = owner.Pos + new Vector2(195, 0);

        world.GiveItem(owner, hood);
        world.Step(default);
        TestWorld.Close(0.08f, owner.Stats[Stat.Lifesteal]);
        TestWorld.Close(0, ally.Stats[Stat.Lifesteal]);

        world.GiveItem(owner, hood);
        world.Step(default);
        TestWorld.Close(0.08f, owner.Stats[Stat.Lifesteal]);
        TestWorld.Close(0.08f, ally.Stats[Stat.Lifesteal]);

        world.GiveItem(ally, hood);
        world.Step(default);
        TestWorld.Close(0.08f, owner.Stats[Stat.Lifesteal]);
        TestWorld.Close(0.08f, ally.Stats[Stat.Lifesteal]);
    }

    private static ContentSet QuietContent()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        content.Enemies[0].Damage = 0;
        content.Enemies[0].Hp = 1000;
        content.Enemies[0].Speed = 0;
        return content;
    }
}
