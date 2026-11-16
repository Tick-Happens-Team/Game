using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

internal static class SimulationBenchmark
{
    private const int EnemyCount = 2000, WarmupTicks = 300, SampleTicks = 600;

    public static void Run(ContentSet content, string reportPath)
    {
        // Keep the crowd at a fixed size while still exercising movement, collisions and auto-attacks.
        // This content instance is private to the benchmark.
        content.Waves.MaxAlive = EnemyCount;
        content.Waves.Elites.Clear();
        int bat = content.EnemyIndex("gloombat");
        content.Enemies[bat].Damage = 0;
        content.Enemies[bat].Hp = 1_000_000;
        var world = new RunWorld(content, seed: 1, character: "octarine");
        for (int i = 0; i < EnemyCount; i++)
        {
            float angle = i * MathF.Tau / EnemyCount;
            float radius = 100 + 4 * (i % 75);
            world.SpawnEnemy(bat, world.Player.Pos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        for (int i = 0; i < WarmupTicks; i++) Step(world);
        var times = new double[SampleTicks];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < SampleTicks; i++)
        {
            long start = Stopwatch.GetTimestamp();
            Step(world);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (world.AliveEnemies != EnemyCount)
                throw new InvalidOperationException($"Expected {EnemyCount} enemies at tick {world.Tick}.");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        string path = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var report = new StreamWriter(path))
        {
            report.WriteLine("tick,enemies,step_ms");
            for (int i = 0; i < times.Length; i++)
                report.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{WarmupTicks + i + 1},{EnemyCount},{times[i]:F6}"));
        }

        Array.Sort(times);
        double p99 = times[(int)Math.Ceiling(0.99 * times.Length) - 1];
        Console.WriteLine(FormattableString.Invariant(
            $"{EnemyCount} enemies, {SampleTicks} ticks: min {times[0]:F3} ms, avg {times.Average():F3} ms, p99 {p99:F3} ms, max {times[^1]:F3} ms, {allocated} bytes allocated"));
        Console.WriteLine(path);
    }

    private static void Step(RunWorld world)
    {
        world.Step(default);
        world.ClearEvents();
    }
}
