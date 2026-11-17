using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

/// <summary>
/// Character stats: value = (base + Σadd) × (1 + Σpct).
/// Static modifiers come from items; the dynamic layer is recomputed every tick from level- and time-scaled passives.
/// Additive and percentage parts are accumulated separately, so the result does not depend on order.
/// </summary>
public sealed class StatBlock
{
    private static readonly int Count = Enum.GetValues<Stat>().Length;
    private readonly float[] _base = new float[Count];
    private readonly float[] _add = new float[Count];
    private readonly float[] _pct = new float[Count];
    private readonly float[] _dynAdd = new float[Count];
    private readonly float[] _dynPct = new float[Count];

    public StatBlock()
    {
        foreach (var s in new[] { Stat.HpMultiplier, Stat.Might, Stat.PhysicalDamage, Stat.MagicalDamage, Stat.ItemDamage,
                     Stat.Area, Stat.Cooldown, Stat.GoldGain, Stat.DashCooldown, Stat.EliteDamage, Stat.HighestHealthDamage, Stat.DashLength, Stat.DamageTaken })
            _base[(int)s] = 1;
        _base[(int)Stat.PhysicalCrit] = 0.05f;
        _base[(int)Stat.MagicalCrit] = 0.05f;
        _base[(int)Stat.CritDamage] = 2;
        _base[(int)Stat.LifestealHealCap] = 0.05f;
    }

    public void SetBase(Stat s, float v) => _base[(int)s] = v;
    public float Base(Stat s) => _base[(int)s];

    /// <summary>Permanent modifier (items).</summary>
    public void Apply(Stat s, float add, float pct)
    {
        _add[(int)s] += add;
        _pct[(int)s] += pct;
    }

    public void ClearDynamic()
    {
        Array.Clear(_dynAdd);
        Array.Clear(_dynPct);
    }

    internal void ClearItems()
    {
        Array.Clear(_add);
        Array.Clear(_pct);
    }

    /// <summary>Per-tick modifier (passives that scale with level or the run clock).</summary>
    public void ApplyDynamic(Stat s, float add, float pct)
    {
        _dynAdd[(int)s] += add;
        _dynPct[(int)s] += pct;
    }

    public float this[Stat s]
    {
        get
        {
            int i = (int)s;
            float v = (_base[i] + _add[i] + _dynAdd[i]) * (1 + _pct[i] + _dynPct[i]);
            return s switch
            {
                // Cooldown-like multipliers never drop below 25% of base; other stats never go negative.
                Stat.Cooldown or Stat.DashCooldown => MathF.Max(0.25f, v),
                Stat.DamageTaken => MathF.Max(0.1f, v),
                _ => MathF.Max(0, v),
            };
        }
    }
}
