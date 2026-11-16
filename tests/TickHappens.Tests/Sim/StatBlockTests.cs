using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class StatBlockTests
{
    [Fact]
    public void ValueIsBasePlusAddsTimesOnePlusPcts()
    {
        var stats = new StatBlock();
        stats.SetBase(Stat.Might, 2f);
        stats.Apply(Stat.Might, 3f, 0.5f);
        stats.ApplyDynamic(Stat.Might, 1f, 0.25f);

        Assert.Equal((2f + 3f + 1f) * (1f + 0.5f + 0.25f), stats[Stat.Might]);
    }

    [Fact]
    public void ClearDynamicKeepsStaticModifiers()
    {
        var stats = new StatBlock();
        stats.SetBase(Stat.MaxHp, 100f);
        stats.Apply(Stat.MaxHp, 20f, 0.1f);
        stats.ApplyDynamic(Stat.MaxHp, 5f, 0.2f);
        Assert.Equal((100f + 20f + 5f) * (1f + 0.1f + 0.2f), stats[Stat.MaxHp]);

        stats.ClearDynamic();

        Assert.Equal((100f + 20f) * (1f + 0.1f), stats[Stat.MaxHp]);
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
    public void MultipliersDefaultToOne()
    {
        var stats = new StatBlock();

        foreach (var stat in new[]
                 {
                     Stat.HpMultiplier, Stat.Might, Stat.PhysicalDamage, Stat.MagicalDamage, Stat.ItemDamage,
                     Stat.Area, Stat.Cooldown, Stat.GoldGain, Stat.DashCooldown,
                 })
            Assert.Equal(1f, stats[stat]);

        Assert.Equal(0f, stats[Stat.MaxHp]);
        Assert.Equal(0f, stats[Stat.Armor]);
        Assert.Equal(0f, stats[Stat.MagicResist]);
    }

    [Fact]
    public void CooldownAndDashCooldownNeverDropBelowQuarter()
    {
        var stats = new StatBlock();
        stats.Apply(Stat.Cooldown, 0f, -0.9f);
        stats.Apply(Stat.DashCooldown, 0f, -10f);

        Assert.Equal(0.25f, stats[Stat.Cooldown]);
        Assert.Equal(0.25f, stats[Stat.DashCooldown]);
    }
}
