using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Numerics;
using TickHappens.Game.Content;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

internal static class NetworkBenchmark
{
    private const int EnemyCount = 1000;
    private const int WarmupTicks = RunWorld.TicksPerSecond;
    private const int SampleTicks = 5 * RunWorld.TicksPerSecond;

    public static void Run(ContentSet content, string reportPath)
    {
        content.Waves.MaxAlive = EnemyCount;
        content.Waves.Elites.Clear();
        int bat = content.EnemyIndex("gloombat");
        content.Enemies[bat].Hp = 1_000_000;
        content.Enemies[bat].Damage = 0;

        using var host = new OnlineGame(content, lanPort: 0, collectStatistics: true);
        using var guest = new OnlineGame(content);
        host.ConnectLan(true, null, "octarine", 1);
        var endpoint = new IPEndPoint(IPAddress.Loopback, host.LanPort!.Value);
        guest.ConnectLan(false, endpoint, "swordsman", 1);
        // The run starts from the lobby once both players are ready, as in the game.
        PumpUntil(host, guest, () => guest.CanReady && host.CanReady, "The guest did not join the LAN room.");
        host.ToggleReady();
        guest.ToggleReady();
        PumpUntil(host, guest, () => host.CanStart, "The players did not become ready.");
        host.Start();
        var world = host.World ?? throw new InvalidOperationException(host.Status);
        PumpUntil(host, guest, () => guest.Playing, "The guest did not receive the run.");
        for (int i = 0; i < EnemyCount; i++)
        {
            float angle = i * MathF.Tau / EnemyCount;
            float radius = 100 + 4 * (i % 75);
            world.SpawnEnemy(bat, world.Player.Pos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        var rows = new List<(int Tick, int Input, double Latency, long Bytes)>();
        var sentAt = new Dictionary<int, long>();
        long start = Stopwatch.GetTimestamp(), previous = start;
        long sampleStart = 0, initialBytes = 0;
        int lastAck = 0;
        while (world.Tick < WarmupTicks + SampleTicks && Stopwatch.GetElapsedTime(start).TotalSeconds < 20)
        {
            long now = Stopwatch.GetTimestamp();
            float dt = (float)Math.Min(0.1, Stopwatch.GetElapsedTime(previous, now).TotalSeconds);
            previous = now;
            host.Update(dt, default);
            int sentBefore = guest.LastSubmittedInput;
            guest.Update(dt, new RunInput(world.Tick % 120 < 60 ? 1 : -1, 0));
            world.ClearEvents();
            guest.World?.ClearEvents();

            for (int input = sentBefore + 1; input <= guest.LastSubmittedInput; input++)
                sentAt[input] = now;
            int ack = guest.World?.Player.LastAppliedInput ?? 0;
            if (world.Tick >= WarmupTicks && sampleStart == 0)
            {
                sampleStart = now;
                initialBytes = host.SentLanBytes;
            }
            if (ack > lastAck)
            {
                if (sampleStart != 0 && sentAt.TryGetValue(ack, out long submitted))
                    rows.Add((world.Tick, ack, Stopwatch.GetElapsedTime(submitted, now).TotalMilliseconds,
                        host.SentLanBytes - initialBytes));
                for (int input = lastAck + 1; input <= ack; input++)
                    sentAt.Remove(input);
                lastAck = ack;
            }
            Thread.Sleep(1);
        }

        if (world.Tick < WarmupTicks + SampleTicks || rows.Count == 0)
            throw new InvalidOperationException("LAN benchmark did not complete an authoritative run.");
        double seconds = Stopwatch.GetElapsedTime(sampleStart).TotalSeconds;
        long bytes = host.SentLanBytes - initialBytes;
        string path = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var report = new StreamWriter(path))
        {
            report.WriteLine("host_tick,acknowledged_input,ack_ms,host_sent_bytes");
            foreach (var row in rows)
                report.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{row.Tick},{row.Input},{row.Latency:F3},{row.Bytes}"));
        }
        var latencies = rows.Select(row => row.Latency).Order().ToArray();
        double p99 = latencies[(int)Math.Ceiling(0.99 * latencies.Length) - 1];
        Console.WriteLine(FormattableString.Invariant(
            $"{EnemyCount} enemies, {rows.Count} input acknowledgements: {latencies.Average():F1} ms average, {p99:F1} ms p99, {bytes / seconds / 1000:F1} kB/s host outbound on loopback."));
        Console.WriteLine(path);
    }

    private static void PumpUntil(OnlineGame host, OnlineGame guest, Func<bool> done, string failure)
    {
        var timeout = Stopwatch.StartNew();
        while (!done())
        {
            if (timeout.Elapsed.TotalSeconds > 10) throw new InvalidOperationException(failure);
            host.Update(RunWorld.Dt, default);
            guest.Update(RunWorld.Dt, default);
            Thread.Sleep(1);
        }
    }
}
