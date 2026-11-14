using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

/// <summary>
/// Character stats with ordered modifiers: value = (base + Σadd) × (1 + Σpct).
/// Additive and percentage parts are accumulated separately so the result does not depend on pickup order.
/// </summary>
public sealed class StatBlock
{
    private static readonly int Count = Enum.GetValues<Stat>().Length;
    private readonly float[] _base = new float[Count];
    private readonly float[] _add = new float[Count];
    private readonly float[] _pct = new float[Count];

    public StatBlock()
    {
        // Multipliers default to 1.
        _base[(int)Stat.Might] = 1;
        _base[(int)Stat.Area] = 1;
        _base[(int)Stat.Cooldown] = 1;
    }

    public void SetBase(Stat s, float v) => _base[(int)s] = v;

    public void Apply(Stat s, float add, float pct)
    {
        _add[(int)s] += add;
        _pct[(int)s] += pct;
    }

    public float this[Stat s]
    {
        get
        {
            float v = (_base[(int)s] + _add[(int)s]) * (1 + _pct[(int)s]);
            // Cooldown multiplier never drops below 25% of base; other stats never go negative.
            return s == Stat.Cooldown ? MathF.Max(0.25f, v) : MathF.Max(0, v);
        }
    }

    public static StatBlock ForCharacter(CharacterDef c)
    {
        var b = new StatBlock();
        foreach (var (name, value) in c.Stats)
            if (ContentLoader.TryParseStat(name, out var s)) b.SetBase(s, value);
        return b;
    }
}
