namespace TickHappens.Game;

public sealed class RunLoop(RunWorld world)
{
    private const double StepSeconds = 1d / RunWorld.TicksPerSecond;
    private double _accumulatedSeconds;

    public bool Paused { get; private set; }
    public float Interpolation => (float)(_accumulatedSeconds / StepSeconds);

    public void TogglePause() => Paused = !Paused;

    public void Advance(double frameSeconds, RunInput input)
    {
        if (Paused) return;
        _accumulatedSeconds += Math.Clamp(frameSeconds, 0, 0.25);
        while (_accumulatedSeconds + 1e-9 >= StepSeconds)
        {
            world.Step(input);
            _accumulatedSeconds = Math.Max(0, _accumulatedSeconds - StepSeconds);
        }
    }
}
