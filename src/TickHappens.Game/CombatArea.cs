using System.Numerics;

namespace TickHappens.Game;

/// <summary>Gameplay distances are measured in world pixels from ground pivot to ground pivot.</summary>
public readonly struct CombatArea
{
    private readonly Vector2 _forward;
    private readonly float _cosHalfAngle;
    private readonly bool _isCone;

    private CombatArea(Vector2 origin, float radius, Vector2 forward, float cosHalfAngle, bool isCone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (!float.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        Origin = origin;
        Radius = radius;
        _forward = forward;
        _cosHalfAngle = cosHalfAngle;
        _isCone = isCone;
    }

    public Vector2 Origin { get; }
    public float Radius { get; }

    public static CombatArea Circle(Vector2 origin, float radius) => new(origin, radius, default, 0, false);

    public static CombatArea Cone(Vector2 origin, float radius, Vector2 forward, float angleDegrees)
    {
        if (!(angleDegrees > 0 && angleDegrees < 360)) throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        if (!float.IsFinite(forward.LengthSquared()) || forward.LengthSquared() == 0)
            throw new ArgumentOutOfRangeException(nameof(forward));
        return new(origin, radius, Vector2.Normalize(forward), MathF.Cos(angleDegrees * MathF.PI / 360), true);
    }

    public bool Contains(Vector2 point, out float distanceSquared)
    {
        Vector2 offset = point - Origin;
        distanceSquared = offset.LengthSquared();
        return distanceSquared <= Radius * Radius &&
            (!_isCone || Vector2.Dot(offset, _forward) >= MathF.Sqrt(distanceSquared) * _cosHalfAngle);
    }

    public float WeightAt(Vector2 point)
    {
        if (!Contains(point, out float distanceSquared)) return 0;
        return Radius == 0 ? 1 : Math.Clamp(1 - MathF.Sqrt(distanceSquared) / Radius, 0, 1);
    }
}
