using TickHappens.Game.Sim;

namespace TickHappens.Tests;

/// <summary>Helpers shared by the simulation tests: approximate float checks and direct world setup.</summary>
internal static class TestWorld
{
    /// <summary>Asserts float equality to <paramref name="precision"/> decimal places (simulation math is not exact).</summary>
    public static void Close(float expected, float actual, int precision = 4) =>
        Assert.Equal(expected, actual, precision);

    /// <summary>Places a gold pickup on the player so the next step collects it, without a kill or XP.</summary>
    public static void DropGold(RunWorld world, int amount)
    {
        int slot = Array.IndexOf(world.GAlive, false);
        Assert.True(slot >= 0, "no free pickup slot");
        world.GX[slot] = world.Player.Pos.X;
        world.GY[slot] = world.Player.Pos.Y - 8; // pickups are measured against the player's feet
        world.GKind[slot] = PickupKind.Gold;
        world.GValue[slot] = amount;
        world.GOwner[slot] = world.Player.Id;
        world.GAlive[slot] = true;
    }

    /// <summary>Grants exactly enough XP to reach <paramref name="level"/>.</summary>
    public static void LevelTo(RunWorld world, int level)
    {
        while (world.Player.Level < level) world.GainXp(world.Player.XpNext);
    }
}
