using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class StatBlockTests
{
    [Fact]
    public void ValueIsBasePlusAddTimesOnePlusPct()
    {
        var stats = new StatBlock();
        stats.SetBase(Stat.Might, 2f);
        stats.Apply(Stat.Might, 3f, 0.5f);
        stats.Apply(Stat.Might, 1f, 0.25f);

        Assert.Equal((2f + 3f + 1f) * (1f + 0.5f + 0.25f), stats[Stat.Might]);
    }

    [Fact]
    public void ApplyOrderDoesNotChangeTheResult()
    {
        (float Add, float Pct)[] mods = [(0.5f, 0.25f), (1.5f, 0.5f), (2f, 0.125f), (-1f, -0.0625f)];
        var forward = new StatBlock();
        var backward = new StatBlock();
        forward.SetBase(Stat.MaxHp, 100f);
        backward.SetBase(Stat.MaxHp, 100f);

        foreach (var (add, pct) in mods) forward.Apply(Stat.MaxHp, add, pct);
        for (int i = mods.Length - 1; i >= 0; i--) backward.Apply(Stat.MaxHp, mods[i].Add, mods[i].Pct);

        Assert.Equal((100f + 3f) * (1f + 0.8125f), forward[Stat.MaxHp]);
        Assert.Equal(forward[Stat.MaxHp], backward[Stat.MaxHp]);
    }

    [Fact]
    public void CooldownNeverDropsBelowQuarter()
    {
        var stats = new StatBlock();
        stats.SetBase(Stat.Cooldown, 1f);

        stats.Apply(Stat.Cooldown, 0f, -0.9f);
        Assert.Equal(0.25f, stats[Stat.Cooldown]);

        stats.Apply(Stat.Cooldown, 0f, -10f);
        Assert.Equal(0.25f, stats[Stat.Cooldown]);
    }

    [Fact]
    public void MultipliersDefaultToOne()
    {
        var stats = new StatBlock();

        Assert.Equal(1f, stats[Stat.Might]);
        Assert.Equal(1f, stats[Stat.Area]);
        Assert.Equal(1f, stats[Stat.Cooldown]);
        Assert.Equal(0f, stats[Stat.MaxHp]);
        Assert.Equal(0f, stats[Stat.Armor]);
    }

    [Fact]
    public void ForCharacterUsesCharacterBaseStats()
    {
        var character = TestContent.LoadCore().Characters[0];
        var stats = StatBlock.ForCharacter(character);

        foreach (var (name, value) in character.Stats)
        {
            Assert.True(ContentLoader.TryParseStat(name, out var stat), $"character stat '{name}' is not a known stat");
            Assert.Equal(value, stats[stat]);
        }

        Assert.Equal(1f, stats[Stat.Might]);
        Assert.Equal(1f, stats[Stat.Area]);
    }
}
