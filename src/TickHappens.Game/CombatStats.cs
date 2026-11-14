namespace TickHappens.Game;

public enum DamageType { Physical, Magical, Pure }

/// <summary>Resolved multipliers. Radius bonuses affect item and weapon areas, separately from interaction reach.</summary>
public readonly record struct CombatStats(float RadiusMultiplier, float PhysicalDamageMultiplier,
    float MagicalDamageMultiplier, float PureDamageMultiplier)
{
    public static CombatStats Base => new(1, 1, 1, 1);

    public float EffectRadius(float baseRadius) => baseRadius * RadiusMultiplier;

    public float OutgoingDamage(float baseDamage, DamageType type) => baseDamage * (type switch
    {
        DamageType.Physical => PhysicalDamageMultiplier,
        DamageType.Magical => MagicalDamageMultiplier,
        DamageType.Pure => PureDamageMultiplier,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    });
}

public readonly record struct DamageSource(int PlayerId, int? SummonOwnerId = null)
{
    public int CreditPlayerId => SummonOwnerId ?? PlayerId;
}

public readonly record struct DamageHit(DamageSource Source, DamageType Type, float Amount);
