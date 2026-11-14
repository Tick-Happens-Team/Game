using System.Numerics;
using TickHappens.Game;

namespace TickHappens.Tests;

public sealed class CombatAreaTests
{
    [Fact]
    public void CircleIncludesBoundaryAndFallsOffLinearly()
    {
        var area = CombatArea.Circle(new(10, 20), 50);
        Assert.True(area.Contains(new(60, 20), out float distanceSquared));
        Assert.Equal(2500, distanceSquared);
        Assert.False(area.Contains(new(61, 20), out _));
        Assert.Equal(1, area.WeightAt(new(10, 20)));
        Assert.Equal(0.5f, area.WeightAt(new(35, 20)));
        Assert.Equal(0, area.WeightAt(new(60, 20)));
    }

    [Fact]
    public void ConeUsesFullAngleAndNormalizedFacing()
    {
        var area = CombatArea.Cone(Vector2.Zero, 100, new(4, 0), 45);
        Assert.True(area.Contains(new(50, 20), out _));
        Assert.False(area.Contains(new(50, 21), out _));
        Assert.False(area.Contains(new(-50, 0), out _));
        Assert.False(area.Contains(new(101, 0), out _));
    }

    [Fact]
    public void RadiusModifierExtendsAutoAttackAndTypedDamageCanRequireMultipleHits()
    {
        var world = new RunWorld
        {
            Stats = CombatStats.Base with { RadiusMultiplier = 0.7f, PhysicalDamageMultiplier = 0.5f },
        };
        world.Step(default);
        Assert.True(world.IsAlive(0));
        world.Stats = world.Stats with { RadiusMultiplier = 0.7f * 1.15f };
        world.Step(default);
        Assert.True(world.IsAlive(0));
        for (int i = 0; i < 15; i++) world.Step(default);
        Assert.False(world.IsAlive(0));
        Assert.True(world.HasGoldDrop(0));
    }

    [Fact]
    public void SummonKillCreditSurvivesLaterHits()
    {
        var world = new RunWorld();
        world.ApplyDamage(0, new(new(0, SummonOwnerId: 1), DamageType.Magical, 15));
        world.ApplyDamage(0, new(new(0), DamageType.Pure, 15));
        for (int i = 0; i < 14; i++) world.Step(new(0, -1));
        Assert.True(world.HasGoldDrop(0));
        Assert.Equal(0, world.Wallet.Gold);
        Assert.False(world.IsOpen(0));
    }
}
