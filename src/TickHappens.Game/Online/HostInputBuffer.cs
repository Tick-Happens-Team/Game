using TickHappens.Game.Sim;

namespace TickHappens.Game.Online;

/// <summary>
/// One guest's inputs on the host, consumed one per simulation tick in sequence order. The guest predicts its hero by
/// applying each input exactly once per tick and replays every input newer than <see cref="Applied"/>, so the host
/// must apply each input once and acknowledge only what it applied. Inputs that arrive in a burst wait in the queue
/// instead of overwriting each other. When the queue runs dry the host repeats the last input without
/// acknowledging anything, and the queue then holds one more input, which absorbs later jitter. A queue that grows
/// past <see cref="MaxDepth"/> (the guest's clock ran ahead or the host stalled) is trimmed to
/// <see cref="TrimDepth"/> while dash and chest actions of the skipped inputs carry over. A queue that never fell
/// below <see cref="TrimDepth"/> for <see cref="ShrinkTicks"/> ticks drops one input, so delay the jitter once needed
/// does not stay forever.
/// </summary>
internal sealed class HostInputBuffer
{
    public const int Capacity = 32, MaxDepth = 6, TrimDepth = 2, ShrinkTicks = RunWorld.TicksPerSecond;
    // A guest silent for this many ticks stops moving instead of walking on with its last input.
    public const int IdleTicks = RunWorld.TicksPerSecond / 3;

    private readonly RunInput[] _inputs = new RunInput[Capacity];
    private readonly int[] _sequences = new int[Capacity];
    private int _head, _count, _lastQueued, _windowTicks, _windowMinimum = int.MaxValue;
    private RunInput _last;

    /// <summary>Sequence of the newest input applied to the simulation; snapshots acknowledge it to the guest.</summary>
    public int Applied { get; private set; }
    public int Depth => _count;
    /// <summary>Consecutive ticks without a queued input.</summary>
    public int StarvedTicks { get; private set; }

    public void Reset()
    {
        _head = _count = _lastQueued = _windowTicks = 0;
        _windowMinimum = int.MaxValue;
        Applied = StarvedTicks = 0;
        _last = default;
    }

    public void Enqueue(int sequence, RunInput input)
    {
        if (sequence <= _lastQueued || sequence <= Applied) return;
        _lastQueued = sequence;
        if (_count == Capacity) DropOldest();
        int tail = (_head + _count) % Capacity;
        _inputs[tail] = input;
        _sequences[tail] = sequence;
        _count++;
    }

    /// <summary>The input for the next host tick.</summary>
    public RunInput Next()
    {
        if (_count > MaxDepth)
            while (_count > TrimDepth) DropOldest();
        _windowMinimum = Math.Min(_windowMinimum, _count);
        if (++_windowTicks >= ShrinkTicks)
        {
            if (_windowMinimum >= TrimDepth) DropOldest();
            _windowTicks = 0;
            _windowMinimum = int.MaxValue;
        }
        if (_count == 0)
        {
            if (++StarvedTicks > IdleTicks) _last = default;
            return _last with { Dash = false, ChestChoice = 0, OpenChest = false };
        }
        StarvedTicks = 0;
        var input = _inputs[_head];
        Applied = _sequences[_head];
        _head = (_head + 1) % Capacity;
        _count--;
        _last = input;
        return input;
    }

    private void DropOldest()
    {
        var dropped = _inputs[_head];
        Applied = _sequences[_head];
        _head = (_head + 1) % Capacity;
        _count--;
        if (_count == 0)
        {
            _last = dropped;
            return;
        }
        ref var next = ref _inputs[_head];
        next = next with
        {
            Dash = next.Dash || dropped.Dash,
            OpenChest = next.OpenChest || dropped.OpenChest,
            ChestChoice = next.ChestChoice != 0 ? next.ChestChoice : dropped.ChestChoice,
        };
    }
}
