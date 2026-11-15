using System.Numerics;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const float RevivalRadius = 100;
    public const int RevivalHoldTicks = 2 * TicksPerSecond;
    private const float RevivalHealthFraction = 0.15f;

    private void StepRevival()
    {
        int revived = 0;
        foreach (var fallen in _players)
        {
            if (!fallen.Active || fallen.Hp > 0) continue;

            bool assisted = false;
            foreach (var ally in _players)
            {
                if (!ally.Active || ally.Hp <= 0 || ally.Walking ||
                    Vector2.DistanceSquared(ally.Pos, fallen.Pos) > RevivalRadius * RevivalRadius) continue;
                assisted = true;
                break;
            }

            if (!assisted)
            {
                fallen.RevivalTicks = 0;
                continue;
            }
            if (++fallen.RevivalTicks >= RevivalHoldTicks) revived |= 1 << fallen.Id;
        }
        if (revived == 0) return;

        foreach (var fallen in _players)
        {
            if ((revived & (1 << fallen.Id)) == 0) continue;
            fallen.RevivalTicks = 0;
            fallen.Prev = fallen.Pos;
            fallen.Walking = false;
            fallen.DashTicks = fallen.DashSpeedTicks = fallen.AcceleratorTicks = 0;
            fallen.Hp = 1;
            fallen.InvulnTicks = TicksPerSecond;
        }

        // Reapply current health auras now that the revived players are alive.
        UpdateAllPlayerStats();
        foreach (var fallen in _players)
        {
            if ((revived & (1 << fallen.Id)) == 0) continue;
            fallen.Hp = MaxHealthFor(fallen) * RevivalHealthFraction;
            Emit(EventKind.Revival, fallen.Pos.X, fallen.Pos.Y);
        }
    }
}
