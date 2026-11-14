using System.Numerics;

namespace TickHappens.Game.Sim;

/// <summary>
/// Deterministic autopilot used by --demo, screenshots and long-run tests: kites the crowd,
/// collects crystals, visits statues after the first minute, and buys chests it can afford.
/// </summary>
public static class DemoPilot
{
    public static RunInput Decide(RunWorld w)
    {
        if (w.Phase == RunPhase.Dead) return default;
        var p = w.Player.Pos;
        var steer = Vector2.Zero;
        float closest = float.MaxValue;
        for (int i = 0; i < w.EnemyHighWater; i++)
        {
            if (!w.EAlive[i]) continue;
            var d = p - new Vector2(w.EX[i], w.EY[i]);
            float l2 = d.LengthSquared();
            closest = MathF.Min(closest, l2);
            if (l2 < 90 * 90 && l2 > 0.01f) steer += d / l2 * 60;
        }
        // Drift on a wide orbit around the arena center so the bot keeps moving through fresh ground.
        var center = new Vector2(w.Rules.ArenaSize / 2f);
        var toC = center - p;
        var tangent = Vector2.Normalize(new Vector2(-toC.Y, toC.X) + new Vector2(0.001f));
        steer += tangent * 0.6f + (toC.Length() > 300 ? Vector2.Normalize(toC) * 0.5f : Vector2.Zero);
        // Pull towards the nearest crystal or (after a minute) the nearest dormant statue.
        Vector2? goal = null;
        float best = 140 * 140;
        for (int i = 0; i < RunWorld.PickupCapacity; i++)
        {
            if (!w.GAlive[i]) continue;
            float d2 = Vector2.DistanceSquared(p, new Vector2(w.GX[i], w.GY[i]));
            if (d2 < best) { best = d2; goal = new Vector2(w.GX[i], w.GY[i]); }
        }
        if (w.Minutes > 1)
            for (int i = 0; i < w.Statues.Length; i++)
                if (!w.StatueActive[i] && Vector2.DistanceSquared(p, w.Statues[i]) < 260 * 260) goal = w.Statues[i];
        // Visit an affordable chest nearby (items come only from chests).
        if (w.Player.Wallet.Gold >= w.Player.Wallet.NextPrice)
            for (int i = 0; i < w.Chests.Length; i++)
                if (!w.ChestOpen[i] && Vector2.DistanceSquared(p, w.Chests[i]) < 320 * 320) goal = w.Chests[i];
        if (goal is { } g && g != p) steer += Vector2.Normalize(g - p) * 0.9f;
        int x = steer.X > 0.3f ? 1 : steer.X < -0.3f ? -1 : 0;
        int y = steer.Y > 0.3f ? 1 : steer.Y < -0.3f ? -1 : 0;
        bool dash = closest < 14 * 14;
        return new RunInput(x, y, dash);
    }
}
