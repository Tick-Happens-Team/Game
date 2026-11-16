using System.Numerics;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

/// <summary>
/// Guest prediction against the host's input handling: the guest moves its hero as soon as it submits an input and,
/// on each snapshot, replays the inputs the host has not acknowledged yet. A visible correction (a "teleport") is the
/// distance between the predicted position before and after a snapshot.
/// </summary>
public sealed class NetcodePredictionTests
{
    private static readonly RunInput[] Pattern =
    [
        new(1, 0), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1),
    ];

    private static RunInput GuestInput(int sequence) => Pattern[sequence / 7 % Pattern.Length];

    [Fact]
    public void BurstyInputArrivalCausesNoCorrection()
    {
        // Inputs arrive in clumps of up to four with gaps between them, but the host never runs dry.
        int[] arrival = ArrivalTicks(240, sequence => sequence + new[] { 0, 3, 1, 3, 2, 0, 3, 3, 1 }[sequence % 9]);
        var result = Simulate(arrival, hostStartTick: 4);

        Assert.Equal(0, result.StarvedTicks);
        Assert.InRange(result.MaxCorrection, 0, 0.1f);
    }

    [Fact]
    public void DelaySpikeCorrectsByAtMostAFewStepsAndSettles()
    {
        // A 300 ms stall delivers nine inputs at once; the host repeats the last input meanwhile.
        int[] arrival = ArrivalTicks(240, sequence => sequence is >= 90 and < 99 ? 99 + 2 : sequence + 2);
        var result = Simulate(arrival, hostStartTick: 3);

        Assert.InRange(result.StarvedTicks, 1, 12);
        float step = result.StepDistance;
        Assert.InRange(result.MaxCorrection, 0, step * 10);
        // Afterwards the host sheds the extra queued delay one input at a time: at most one step of walking.
        Assert.InRange(result.LateCorrection, 0, step * 1.05f);
    }

    [Fact]
    public void PredictedRootEndsOnTheSameTickAsOnTheHost()
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, seed: 7, playerCount: 4);
        host.SetPlayerActive(1, true);
        host.Players[1].EnemyRootUntil = host.Tick + 5;
        var guest = new RunWorld(content, seed: 7, playerCount: 4, localPlayerId: 1);
        var snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(guest.TryApplySnapshot(snapshot.AsSpan(0, host.WriteSnapshot(snapshot))));

        var inputs = new RunInput[4];
        inputs[1] = new RunInput(1, 0);
        for (int sequence = 1; sequence <= 10; sequence++)
        {
            host.StepCoop(inputs);
            guest.PredictLocalMovement(inputs[1], sequence);
        }

        Assert.True(host.Players[1].Pos.X > host.Players[1].Prev.X);
        Assert.InRange(Vector2.Distance(host.Players[1].Pos, guest.Player.Pos), 0, 0.05f);
    }

    [Fact]
    public void InputBufferAppliesEachInputOnceAndAcknowledgesOnlyAppliedInputs()
    {
        var buffer = new HostInputBuffer();
        Assert.Equal(new RunInput(0, 0), buffer.Next());
        Assert.Equal(0, buffer.Applied);

        buffer.Enqueue(1, new RunInput(1, 0));
        buffer.Enqueue(2, new RunInput(1, 0, Dash: true));
        buffer.Enqueue(2, new RunInput(-1, 0));
        buffer.Enqueue(1, new RunInput(-1, 0));
        Assert.Equal(new RunInput(1, 0), buffer.Next());
        Assert.Equal(1, buffer.Applied);
        Assert.Equal(new RunInput(1, 0, Dash: true), buffer.Next());
        Assert.Equal(2, buffer.Applied);

        // Running dry repeats the movement without the one-shot dash and without acknowledging a new input.
        Assert.Equal(new RunInput(1, 0), buffer.Next());
        Assert.Equal(2, buffer.Applied);
        Assert.Equal(1, buffer.StarvedTicks);

        for (int tick = 0; tick < HostInputBuffer.IdleTicks; tick++) buffer.Next();
        Assert.Equal(new RunInput(0, 0), buffer.Next());
    }

    [Fact]
    public void OverfullInputBufferTrimsAndKeepsSkippedActions()
    {
        var buffer = new HostInputBuffer();
        buffer.Enqueue(1, new RunInput(1, 0, Dash: true));
        buffer.Enqueue(2, new RunInput(1, 0, ChestChoice: 2));
        for (int sequence = 3; sequence <= HostInputBuffer.MaxDepth + 1; sequence++)
            buffer.Enqueue(sequence, new RunInput(0, 1));

        var input = buffer.Next();
        Assert.True(input.Dash);
        Assert.Equal(2, input.ChestChoice);
        Assert.Equal(HostInputBuffer.MaxDepth + 1 - HostInputBuffer.TrimDepth + 1, buffer.Applied);
        Assert.Equal(HostInputBuffer.TrimDepth - 1, buffer.Depth);
    }

    private static int[] ArrivalTicks(int count, Func<int, int> planned)
    {
        var arrival = new int[count + 1];
        for (int sequence = 1; sequence <= count; sequence++)
            arrival[sequence] = Math.Max(arrival[sequence - 1], planned(sequence));
        return arrival;
    }

    private sealed record Result(float MaxCorrection, float LateCorrection, int StarvedTicks, float StepDistance);

    // Ticks of both sides run in lockstep: on tick t the guest submits input t, the host receives every input whose
    // arrival tick has come and, from hostStartTick on, steps once. Snapshots reach the guest three ticks later. The
    // guest keeps submitting for the whole run, as the game does, so the host never idles on a stale input.
    private static Result Simulate(int[] arrival, int hostStartTick)
    {
        var content = TestContent.LoadCore();
        var host = new RunWorld(content, seed: 7, playerCount: 4);
        host.SetPlayerActive(1, true);
        var guest = new RunWorld(content, seed: 7, playerCount: 4, localPlayerId: 1);
        var buffer = new HostInputBuffer();
        var hostInputs = new RunInput[4];
        var history = new RunInput[arrival.Length];
        var snapshot = new byte[RunWorld.SnapshotCapacity];
        var inFlight = new Queue<(int Arrival, byte[] Bytes)>();
        int count = arrival.Length - 1, received = 0, starved = 0;
        float maxCorrection = 0, lateCorrection = 0;

        int write = host.WriteSnapshot(snapshot);
        Assert.True(guest.TryApplySnapshot(snapshot.AsSpan(0, write)));
        float step = RunWorld.Dt * content.Rules.Units.MoveSpeedScale * content.Characters[0].Stats.MoveSpeed;

        for (int tick = 1; tick <= count; tick++)
        {
            history[tick] = GuestInput(tick);
            guest.PredictLocalMovement(history[tick], tick);
            while (received < count && arrival[received + 1] <= tick)
            {
                received++;
                buffer.Enqueue(received, history[received]);
            }
            if (tick >= hostStartTick)
            {
                hostInputs[1] = buffer.Next();
                if (buffer.StarvedTicks > 0 && buffer.Applied > 0 && buffer.Applied < count) starved++;
                host.Players[1].LastAppliedInput = buffer.Applied;
                host.StepCoop(hostInputs);
                if (host.Tick % 3 == 0)
                {
                    int size = host.WriteSnapshot(snapshot);
                    inFlight.Enqueue((tick + 3, snapshot.AsSpan(0, size).ToArray()));
                }
            }
            while (inFlight.Count > 0 && inFlight.Peek().Arrival <= tick)
            {
                var before = guest.Player.Pos;
                Assert.True(guest.TryApplySnapshot(inFlight.Dequeue().Bytes));
                for (int sequence = guest.Player.LastAppliedInput + 1; sequence <= tick; sequence++)
                    guest.PredictLocalMovement(history[sequence], sequence);
                float correction = Vector2.Distance(before, guest.Player.Pos);
                maxCorrection = Math.Max(maxCorrection, correction);
                if (tick > 150) lateCorrection = Math.Max(lateCorrection, correction);
            }
        }
        Assert.InRange(buffer.Applied, count - 12, count);
        return new Result(maxCorrection, lateCorrection, starved, step);
    }
}
