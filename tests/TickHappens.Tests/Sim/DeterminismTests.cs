using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class DeterminismTests
{
    [Fact]
    public void DemoPilotRunsAreDeterministic()
    {
        var content = TestContent.LoadCore();

        var first = RunDemo(content, seed: 42, steps: 3000);
        var second = RunDemo(content, seed: 42, steps: 3000);
        var other = RunDemo(content, seed: 43, steps: 3000);

        Assert.True(first.Tick > 0, "the demo run must advance the clock");
        Assert.True(first.Kills > 0, "the demo run must kill enemies");
        Assert.Equal(first.Tick, second.Tick);
        Assert.Equal(first.Kills, second.Kills);
        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEqual(first.Hash, other.Hash);
    }

    [Fact]
    public void TwoMinuteDemoRunStaysPlayable()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 42);
        int steps = RunWorld.TicksPerSecond * 120;
        for (int i = 0; i < steps; i++) world.Step(DemoPilot.Decide(world));

        Assert.True(world.Tick > 0, "the run clock must advance");
        Assert.True(world.Minutes > 1f, $"two minutes of ticks should pass a minute of run time, got {world.Minutes}");
        Assert.True(world.Player.Kills > 0, "the demo should kill enemies");
        Assert.True(world.Player.Level > 1, "the demo should level up");
        Assert.True(world.AliveEnemies > 0 || world.Phase == RunPhase.Dead,
            "the arena should still be populated unless the run ended");
    }

    private static (ulong Hash, int Tick, int Kills) RunDemo(ContentSet content, uint seed, int steps)
    {
        var world = new RunWorld(content, seed);
        for (int i = 0; i < steps; i++) world.Step(DemoPilot.Decide(world));
        return (world.StateHash(), world.Tick, world.Player.Kills);
    }
}
