using TickHappens.Game;

namespace TickHappens.Tests;

public sealed class RunWorldTests
{
    [Fact]
    public void ChestRequiresProximityAndOpensOncePerRun()
    {
        var world = new RunWorld();
        world.Step(new(0, 0, true));
        Assert.Equal(0, world.OpenedCount);

        for (int i = 0; i < 14; i++) world.Step(new(0, -1));
        Assert.Equal(0, world.NearbyChest);
        world.Step(new(0, 0, true));
        Assert.True(world.IsOpen(0));
        Assert.Equal(0, world.LastOpenedChest);
        world.Step(new(0, 0, true));
        Assert.Equal(1, world.OpenedCount);
        Assert.False(new RunWorld().IsOpen(0));
    }

    [Fact]
    public void FixedStepProducesSameStateAtDifferentFrameRates()
    {
        var fast = PlayAt(120);
        var slow = PlayAt(24);
        Assert.Equal(10_000, fast.Tick);
        Assert.Equal(fast.Tick, slow.Tick);
        Assert.Equal(fast.Hero, slow.Hero);
        Assert.Equal(fast.PreviousHero, slow.PreviousHero);
        Assert.Equal(1, fast.OpenedCount);
        Assert.Equal(fast.OpenedCount, slow.OpenedCount);
        Assert.Equal(fast.LastOpenedTick, slow.LastOpenedTick);
    }

    [Fact]
    public void InteractionSurvivesFramesWithoutTicksAndPauseFreezesTheRun()
    {
        var world = new RunWorld();
        var loop = new RunLoop(world);
        for (int i = 0; i < 14; i++) world.Step(new(0, -1));
        loop.Advance(1d / 120, new(0, 0, true));
        Assert.Equal(0, world.OpenedCount);
        loop.Advance(1d / 40, default);
        Assert.Equal(1, world.OpenedCount);

        loop.TogglePause();
        loop.Advance(0.25, new(1, 0, true));
        Assert.Equal(15, world.Tick);
        loop.TogglePause();
        loop.Advance(1d / 30, default);
        Assert.Equal(16, world.Tick);
    }

    private static RunWorld PlayAt(int fps)
    {
        var world = new RunWorld();
        var loop = new RunLoop(world);
        for (int frame = 0; frame < 10_000 * fps / 30; frame++)
        {
            bool approaching = frame < fps / 2;
            loop.Advance(1d / fps, new(0, approaching ? -1 : 0, frame == fps / 2));
        }
        return world;
    }
}
