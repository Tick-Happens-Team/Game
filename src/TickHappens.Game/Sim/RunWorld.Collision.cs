using System.Numerics;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private const float ContactTolerance = 0.25f;
    private Vector2 MoveWithTeammateCollision(PlayerState player, Vector2 destination)
    {
        var position = player.Pos;
        var remaining = destination - position;
        for (int pass = 0; pass < 2 && remaining.LengthSquared() > 0.000001f; pass++)
        {
            var sweep = new TeammateSweep(position, remaining);
            foreach (var other in _players)
                if (other.Id != player.Id && other.Active && other.Hp > 0)
                    sweep.Consider(other.Pos);

            position += remaining * sweep.Time;
            if (sweep.Time == 1) break;
            remaining *= 1 - sweep.Time;
            remaining -= sweep.Normal * Vector2.Dot(remaining, sweep.Normal);
        }
        return position;
    }

    private struct TeammateSweep(Vector2 origin, Vector2 move)
    {
        public float Time = 1;
        public Vector2 Normal;

        public void Consider(Vector2 center)
        {
            var offset = origin - center;
            float projection = Vector2.Dot(offset, move);
            if (projection >= 0) return;

            float limit = 2 * PlayerRadius;
            float c = MathF.Max(0, offset.LengthSquared() - limit * limit);
            float discriminant = projection * projection - move.LengthSquared() * c;
            if (discriminant <= 0) return;
            float time = c / (-projection + MathF.Sqrt(discriminant));
            if (time >= Time) return;

            Time = time;
            Normal = Vector2.Normalize(offset + move * time);
        }
    }
}
