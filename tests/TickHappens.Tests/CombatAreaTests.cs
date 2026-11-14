using TickHappens.Game.Content;
using TickHappens.Game.Sim;
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
    public void AreaStatExtendsWeaponRadius()
    {
        var content = ContentLoader.Load(ContentLoader.FindCore());
        var world = new RunWorld(content);
        var p = world.Player;
        p.Weapons.Clear();
        p.Weapons.Add(new WeaponState(content.WeaponIndex("tick_pulse"), 1));
        float baseArea = content.Weapons[content.WeaponIndex("tick_pulse")].Levels[0].Area;
        int enemy = world.SpawnEnemy(content.EnemyIndex("amberslime"), p.Pos + new System.Numerics.Vector2(baseArea + 4, 0));
        float hp = world.EHp[enemy];
        world.Step(default);
        Assert.Equal(hp, world.EHp[enemy]);
        p.Stats.Apply(Stat.Area, 0, 0.5f);
        world.Player.Weapons[0].Timer = 0;
        world.EX[enemy] = p.Pos.X + baseArea + 4;
        world.EY[enemy] = p.Pos.Y;
        world.Step(default);
        Assert.True(world.EHp[enemy] < hp);
    }
}
