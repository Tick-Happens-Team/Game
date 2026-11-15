using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class NekochanTests
{
    [Fact]
    public void TwoNekotyansStackTheirAuras()
    {
        var content = QuietContent();
        var world = new RunWorld(content, character: "nekochan", playerCount: 2);
        world.SetPlayerActive(1, true);
        world.StepCoop([default, default]);

        TestWorld.Close(1 - 0.08f * 1.75f - 0.08f, world.Player.Stats[Stat.DamageTaken]);
    }

    [Fact]
    public void AuraLowersDamageTakenAndAddsRegenForNearAlliesAndMoreForHerself()
    {
        var content = QuietContent();
        var world = new RunWorld(content, character: "nekochan", playerCount: 2);
        world.SetPlayerActive(1, true);
        world.SetStartingHero(1, content.CharacterIndex("octarine"));
        var self = world.Player;
        var ally = world.Players[1];
        ally.Pos = self.Pos + new Vector2(150, 0);
        world.StepCoop([default, default]);

        TestWorld.Close(1 - 0.08f * 1.75f, self.Stats[Stat.DamageTaken]);
        TestWorld.Close(0.15f + 1.75f * 1.75f, self.Stats[Stat.Regen]);
        TestWorld.Close(0.92f, ally.Stats[Stat.DamageTaken]);
        TestWorld.Close(2f + 1.75f, ally.Stats[Stat.Regen]);

        ally.Pos = self.Pos + new Vector2(201, 0);
        world.StepCoop([default, default]);
        TestWorld.Close(1f, ally.Stats[Stat.DamageTaken]);
        TestWorld.Close(2f, ally.Stats[Stat.Regen]);
    }

    [Fact]
    public void AuraGrowsPerLevel()
    {
        var content = QuietContent();
        var world = new RunWorld(content, character: "nekochan", playerCount: 2);
        world.SetPlayerActive(1, true);
        world.SetStartingHero(1, content.CharacterIndex("octarine"));
        TestWorld.LevelTo(world, 5);
        var self = world.Player;
        var ally = world.Players[1];
        ally.Pos = self.Pos + new Vector2(204, 0); // radius 200 + 4 × 1.25 = 205
        world.StepCoop([default, default]);

        TestWorld.Close(1 - (0.08f + 4 * 0.003f), ally.Stats[Stat.DamageTaken]);
        TestWorld.Close(2f + 1.75f + 4 * 0.25f, ally.Stats[Stat.Regen]);
        TestWorld.Close(1 - (0.08f + 4 * 0.003f) * 1.75f, self.Stats[Stat.DamageTaken]);
    }

    [Fact]
    public void EnemyHitsAreReducedAfterDefences()
    {
        var content = QuietContent();
        content.Enemies[0].Speed = 0;
        content.Enemies[0].Hp = 100_000;
        content.Enemies[0].DamageType = "physical";
        var world = new RunWorld(content, character: "nekochan");
        var p = world.Player;
        float full = p.Hp;
        world.SpawnEnemy(0, p.Pos);
        world.Step(default);

        float expected = content.Enemies[0].Damage * world.DamageScale * (1 - 0.025f) * (1 - 0.08f * 1.75f);
        Assert.Equal(expected, full - p.Hp, 2);
    }

    [Fact]
    public void AuraCoversAlliedSummons()
    {
        var content = QuietContent();
        var world = new RunWorld(content, character: "nekochan");
        world.GiveItem(content.ItemIndex("sausages"));
        for (int i = 0; i < 3; i++) world.Step(default);

        var summon = Assert.Single(world.Companions.ToArray(), c => c.Active);
        Assert.True(Vector2.Distance(summon.Pos, world.Player.Pos) < 200);
        TestWorld.Close(0.08f, summon.DamageReduction);
    }

    [Fact]
    public void RegenHealsAWoundedAllyEachSecond()
    {
        var content = QuietContent();
        var world = new RunWorld(content, character: "nekochan", playerCount: 2);
        world.SetPlayerActive(1, true);
        world.SetStartingHero(1, content.CharacterIndex("octarine"));
        var ally = world.Players[1];
        ally.Hp = 50;
        for (int i = 0; i < RunWorld.TicksPerSecond; i++) world.StepCoop([default, default]);

        Assert.Equal(50 + 2f + 1.75f, ally.Hp, 2);
    }

    private static ContentSet QuietContent()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        return content;
    }
}
