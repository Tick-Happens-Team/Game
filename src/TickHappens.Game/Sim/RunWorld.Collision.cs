using System.Numerics;
using TickHappens.Engine.Spatial;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private const float ContactTolerance = 0.25f;
    private readonly float _maxEnemyRadius;
    private float _enemyGridShift;

    private Vector2 MoveBody(Vector2 start, Vector2 destination, float radius, int enemy = -1, int player = -1)
    {
        var position = start;
        var remaining = destination - start;
        for (int pass = 0; pass < 2 && remaining.LengthSquared() > 0.000001f; pass++)
        {
            var sweep = new BodySweep(this, enemy, position, remaining, radius);
            var middle = position + remaining * 0.5f;
            float search = remaining.Length() * 0.5f + radius + _maxEnemyRadius + _enemyGridShift;
            _grid.Query(middle.X, middle.Y, search, ref sweep);
            foreach (var other in _players)
                if (other.Id != player && other.Active && other.Hp > 0)
                    sweep.Consider(other.Pos, PlayerRadius);

            position += remaining * sweep.Time;
            if (sweep.Time == 1) break;
            remaining *= 1 - sweep.Time;
            remaining -= sweep.Normal * Vector2.Dot(remaining, sweep.Normal);
        }
        return position;
    }

    private struct BodySweep(RunWorld world, int self, Vector2 origin, Vector2 move, float radius) : IGridVisitor
    {
        public float Time = 1;
        public Vector2 Normal;

        public void Visit(int index)
        {
            if (index != self && world.EAlive[index])
                Consider(new Vector2(world.EX[index], world.EY[index]), world.Content.Enemies[world.EDef[index]].Radius);
        }

        public void Consider(Vector2 center, float otherRadius)
        {
            var offset = origin - center;
            float projection = Vector2.Dot(offset, move);
            if (projection >= 0) return;

            float limit = radius + otherRadius;
            float c = MathF.Max(0, offset.LengthSquared() - limit * limit);
            float discriminant = projection * projection - move.LengthSquared() * c;
            if (discriminant <= 0) return;
            float time = c / (-projection + MathF.Sqrt(discriminant));
            if (time >= Time) return;

            Time = time;
            Normal = Vector2.Normalize(offset + move * time);
        }
    }

    private Vector2 SeparateFromPlayers(Vector2 position, float radius, int enemy)
    {
        foreach (var player in _players)
        {
            if (!player.Active || player.Hp <= 0) continue;
            var offset = position - player.Pos;
            float distanceSquared = offset.LengthSquared();
            float minimum = radius + PlayerRadius;
            if (distanceSquared >= minimum * minimum) continue;
            var normal = distanceSquared > 0.000001f ? offset / MathF.Sqrt(distanceSquared)
                : enemy % 2 == 0 ? Vector2.UnitX : -Vector2.UnitX;
            position = player.Pos + normal * minimum;
        }
        return position;
    }
}
