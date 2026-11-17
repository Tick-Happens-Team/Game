using System.Numerics;
using TickHappens.Engine.Spatial;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private const float ContactTolerance = 0.25f;
    private const float EnemyBodyScale = 0.65f;
    private readonly Vector2[] _enemyPush;

    private void SeparateEnemies()
    {
        if (AliveEnemies < 2) return;

        BuildEnemyGrid();
        Array.Clear(_enemyPush, 0, _enemyHigh);
        float maxBodyRadius = 0;
        for (int i = 0; i < Content.Enemies.Count; i++)
            maxBodyRadius = MathF.Max(maxBodyRadius, Content.Enemies[i].Radius * EnemyBodyScale);

        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i]) continue;

            var def = Content.Enemies[EDef[i]];
            float bodyRadius = def.Radius * EnemyBodyScale;
            var separation = new EnemySeparation(this, i, bodyRadius, def.Behavior == "vein");
            _grid.Query(EX[i], EY[i], bodyRadius + maxBodyRadius, ref separation);
        }

        var min = new Vector2(4);
        var max = new Vector2(Rules.ArenaSize - 4);
        for (int i = 0; i < _enemyHigh; i++)
        {
            var push = _enemyPush[i];
            if (push == Vector2.Zero) continue;

            const float maxPush = 4;
            if (push.LengthSquared() > maxPush * maxPush) push = Vector2.Normalize(push) * maxPush;
            var position = Vector2.Clamp(new Vector2(EX[i], EY[i]) + push, min, max);
            (EX[i], EY[i]) = (position.X, position.Y);
        }
    }

    private struct EnemySeparation(RunWorld world, int self, float bodyRadius, bool anchored) : IGridVisitor
    {
        public void Visit(int other)
        {
            if (other <= self || !world.EAlive[other]) return;

            var def = world.Content.Enemies[world.EDef[other]];
            bool otherAnchored = def.Behavior == "vein";
            if (anchored && otherAnchored) return;

            var offset = new Vector2(world.EX[self] - world.EX[other], world.EY[self] - world.EY[other]);
            float limit = bodyRadius + def.Radius * EnemyBodyScale;
            float distanceSquared = offset.LengthSquared();
            if (distanceSquared >= limit * limit) return;

            float distance = MathF.Sqrt(distanceSquared);
            var direction = distance > 0 ? offset / distance : Vector2.UnitX;
            var push = direction * (limit - distance);
            if (!anchored) world._enemyPush[self] += otherAnchored ? push : push * 0.5f;
            if (!otherAnchored) world._enemyPush[other] -= anchored ? push : push * 0.5f;
        }
    }

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
