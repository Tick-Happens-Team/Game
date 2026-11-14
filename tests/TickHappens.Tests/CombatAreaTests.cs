using System.Numerics;
using System.Text.Json.Nodes;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

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
    public void AreaStatExtendsItemWeaponRadius()
    {
        string dir = TestContent.CreateScratchCopy();
        try
        {
            TestContent.EditJson(dir, "items.json", root => root.AsArray().Add(JsonNode.Parse("""
                {
                  "id": "test_pulse", "name": "Test Pulse", "rarity": "common", "description": "test pulse",
                  "mods": [],
                  "weapon": { "kind": "pulse", "damageType": "physical", "damage": 5, "cooldown": 1, "area": 40 }
                }
                """)));
            var content = ContentLoader.Load(dir);
            var world = new RunWorld(content, seed: 1);
            int item = content.ItemIndex("test_pulse");
            Assert.True(item >= 0, "the scratch pulse item must load");
            float baseArea = content.Items[item].Weapon!.Area;
            world.GiveItem(item);
            int enemy = world.SpawnEnemy(content.EnemyIndex("duskslime"),
                world.Player.Pos + new Vector2(baseArea + 4, 0));
            float hp = world.EHp[enemy];

            world.ClearEvents();
            world.Step(default);
            var pulse = Assert.Single(world.Events, e => e.Kind == EventKind.Pulse);
            TestWorld.Close(baseArea, pulse.Value);
            Assert.Equal(hp, world.EHp[enemy]); // just outside the base radius

            world.Player.Stats.Apply(Stat.Area, 0, 0.5f);
            world.Player.ItemTimers[item] = 0;
            world.ClearEvents();
            world.Step(default);
            var wider = Assert.Single(world.Events, e => e.Kind == EventKind.Pulse);
            TestWorld.Close(baseArea * 1.5f, wider.Value);
            Assert.Contains(world.Events, e => e.Kind == EventKind.Hit && e.Value == 5f);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
