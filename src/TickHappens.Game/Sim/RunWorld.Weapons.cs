using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const int ProjectileCapacity = 1024;
    private const int HitMemory = 4;

    // ---- projectiles ----
    public readonly float[] PX = new float[ProjectileCapacity], PY = new float[ProjectileCapacity];
    public readonly float[] PVx = new float[ProjectileCapacity], PVy = new float[ProjectileCapacity];
    private readonly float[] _pDamage = new float[ProjectileCapacity], _pLife = new float[ProjectileCapacity];
    private readonly int[] _pPierce = new int[ProjectileCapacity];
    private readonly DamageKind[] _pKind = new DamageKind[ProjectileCapacity];
    private readonly int[] _pItem = new int[ProjectileCapacity];
    private readonly byte[] _pOwner = new byte[ProjectileCapacity];
    public readonly bool[] PAlive = new bool[ProjectileCapacity];
    private readonly int[] _pHits = new int[ProjectileCapacity * HitMemory];
    private int _projCursor;

    private static DamageKind Kind(string s) => ContentLoader.TryParseDamage(s, out var k) ? k : DamageKind.Physical;

    private void StepWeapons(PlayerState p)
    {
        // Basic attack: auto-aimed projectiles, interval from attack speed (С/А).
        p.AttackTimer -= Dt;
        if (p.AttackTimer <= 0)
        {
            var a = p.Attack;
            int count = a.Count + (int)p.Stats[Stat.Amount];
            if (FireBolts(p.Stats[Stat.Damage], Kind(a.DamageType), new DamageSourceRef(p.Id), count, a.Speed, a.Range, a.Pierce))
                p.AttackTimer = 1f / MathF.Max(0.05f, p.Stats[Stat.AttackSpeed] * Rules.Units.AttacksPerUnit);
            else p.AttackTimer = 0; // nothing in range: retry next tick
        }

        // Item weapons: each owned weapon item fires on its own cooldown; extra copies add +1 count.
        for (int i = 0; i < p.ItemStacks.Length; i++)
        {
            if (p.ItemStacks[i] == 0 || Content.Items[i].Weapon is not { } w) continue;
            p.ItemTimers[i] -= Dt;
            if (p.ItemTimers[i] > 0) continue;
            p.ItemTimers[i] = w.Cooldown * p.Stats[Stat.Cooldown];
            var src = new DamageSourceRef(p.Id, i);
            var kind = Kind(w.DamageType);
            int count = w.Count + p.ItemStacks[i] - 1 + (int)p.Stats[Stat.Amount];
            float area = w.Area * p.Stats[Stat.Area];
            switch (w.Kind)
            {
                case "bolt": FireBolts(w.Damage, kind, src, count, w.Speed, w.Range, w.Pierce); break;
                case "pulse": FirePulse(w.Damage * Math.Max(1, count), kind, src, area); break;
                case "chain": FireChain(w.Damage, kind, src, count, w.Range, area); break;
                case "blast": FireBlasts(w.Damage, kind, src, count, w.Range, area); break;
            }
        }
    }

    private int Nearest(Vector2 from, float range, ReadOnlySpan<int> exclude)
    {
        int best = -1;
        float bestD = range * range;
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i]) continue;
            float dx = EX[i] - from.X, dy = EY[i] - from.Y, d2 = dx * dx + dy * dy;
            if (d2 >= bestD) continue;
            bool skip = false;
            foreach (int e in exclude) if (e == i) { skip = true; break; }
            if (skip) continue;
            bestD = d2;
            best = i;
        }
        return best;
    }

    /// <summary>Fires up to <paramref name="count"/> projectiles at the nearest distinct enemies. Returns false when nothing is in range.</summary>
    private bool FireBolts(float damage, DamageKind kind, DamageSourceRef src, int count, float speed, float range, int pierce)
    {
        var p = _players[src.PlayerId];
        count = Math.Clamp(count, 1, 16);
        Span<int> targets = stackalloc int[count];
        int n = 0;
        for (; n < count; n++)
        {
            int t = Nearest(p.Pos, range, targets[..n]);
            if (t < 0) break;
            targets[n] = t;
        }
        if (n == 0) return false;
        for (int k = 0; k < count; k++)
        {
            int t = targets[k % n];
            var dir = new Vector2(EX[t], EY[t] - 6) - (p.Pos + new Vector2(0, -10));
            if (dir.LengthSquared() < 0.01f) dir = p.Facing;
            dir = Vector2.Normalize(dir);
            if (k >= n) dir = Rotate(dir, (k - n + 1) * 0.18f * (k % 2 == 0 ? 1 : -1));
            SpawnProjectile(p.Pos + new Vector2(0, -10), dir * speed, damage, kind, src, pierce, range / speed);
        }
        Emit(EventKind.Bolt, p.Pos.X, p.Pos.Y - 10, n, a: src.ItemDef);
        return true;
    }

    private static Vector2 Rotate(Vector2 v, float a) => new(v.X * MathF.Cos(a) - v.Y * MathF.Sin(a), v.X * MathF.Sin(a) + v.Y * MathF.Cos(a));

    private void SpawnProjectile(Vector2 pos, Vector2 vel, float damage, DamageKind kind, DamageSourceRef src, int pierce, float life)
    {
        int slot = -1;
        for (int k = 0; k < ProjectileCapacity; k++)
        {
            int i = (_projCursor + k) % ProjectileCapacity;
            if (!PAlive[i]) { slot = i; break; }
        }
        if (slot < 0) return; // cap reached: drop the shot (keeps the sim bounded)
        _projCursor = (slot + 1) % ProjectileCapacity;
        PX[slot] = pos.X; PY[slot] = pos.Y; PVx[slot] = vel.X; PVy[slot] = vel.Y;
        _pDamage[slot] = damage; _pPierce[slot] = Math.Max(1, pierce); _pLife[slot] = life;
        _pKind[slot] = kind; _pItem[slot] = src.ItemDef; _pOwner[slot] = (byte)src.PlayerId;
        PAlive[slot] = true;
        _pHits.AsSpan(slot * HitMemory, HitMemory).Clear();
    }

    private struct ProjectileHit(RunWorld w, int proj) : Engine.Spatial.IGridVisitor
    {
        public int Target = -1;
        private float _best = float.MaxValue;
        public void Visit(int j)
        {
            if (!w.EAlive[j]) return;
            var hits = w._pHits.AsSpan(proj * HitMemory, HitMemory);
            foreach (int uid in hits) if (uid == w.EUid[j]) return;
            float r = w.Content.Enemies[w.EDef[j]].Radius + 3;
            float dx = w.EX[j] - w.PX[proj], dy = w.EY[j] - 6 - w.PY[proj], d2 = dx * dx + dy * dy;
            if (d2 < r * r && d2 < _best) { _best = d2; Target = j; }
        }
    }

    private void StepProjectiles()
    {
        for (int i = 0; i < ProjectileCapacity; i++)
        {
            if (!PAlive[i]) continue;
            PX[i] += PVx[i] * Dt;
            PY[i] += PVy[i] * Dt;
            if ((_pLife[i] -= Dt) <= 0) { PAlive[i] = false; continue; }
            var hit = new ProjectileHit(this, i);
            _grid.Query(PX[i], PY[i] + 6, 12, ref hit);
            if (hit.Target < 0) continue;
            int t = hit.Target;
            var hits = _pHits.AsSpan(i * HitMemory, HitMemory);
            for (int k = HitMemory - 1; k > 0; k--) hits[k] = hits[k - 1];
            hits[0] = EUid[t];
            _pPierce[i]--;
            DamageEnemy(t, _pDamage[i], _pKind[i], new DamageSourceRef(_pOwner[i], _pItem[i]), PX[i] - PVx[i], PY[i] - PVy[i], 60);
            if (_pPierce[i] <= 0) PAlive[i] = false;
        }
    }

    private void FirePulse(float damage, DamageKind kind, DamageSourceRef src, float area)
    {
        var p = _players[src.PlayerId].Pos;
        var circle = CombatArea.Circle(p, area);
        for (int i = 0; i < _enemyHigh; i++)
            if (EAlive[i] && circle.Contains(new Vector2(EX[i], EY[i]), out _))
                DamageEnemy(i, damage, kind, src, p.X, p.Y, 90);
        Emit(EventKind.Pulse, p.X, p.Y, area);
    }

    private void FireChain(float damage, DamageKind kind, DamageSourceRef src, int hops, float range, float hopRange)
    {
        hops = Math.Clamp(hops, 1, 16);
        Span<int> chain = stackalloc int[hops];
        int n = 0;
        var position = _players[src.PlayerId].Pos;
        var from = position;
        for (; n < hops; n++)
        {
            int t = Nearest(from, n == 0 ? range : hopRange, chain[..n]);
            if (t < 0) break;
            chain[n] = t;
            from = new Vector2(EX[t], EY[t]);
        }
        if (n == 0) return;
        int start = ChainPoints.Count;
        ChainPoints.Add(position + new Vector2(0, -14));
        for (int k = 0; k < n; k++) ChainPoints.Add(new Vector2(EX[chain[k]], EY[chain[k]] - 6));
        Emit(EventKind.Chain, position.X, position.Y, n, a: start, b: n + 1);
        for (int k = 0; k < n; k++) DamageEnemy(chain[k], damage, kind, src, EX[chain[k]], EY[chain[k]] - 1, 10);
    }

    private struct CountNear(RunWorld w, float x, float y, float radiusSquared) : Engine.Spatial.IGridVisitor
    {
        public int Count;
        public void Visit(int j)
        {
            if (!w.EAlive[j]) return;
            float dx = w.EX[j] - x, dy = w.EY[j] - y;
            if (dx * dx + dy * dy < radiusSquared) Count++;
        }
    }

    private void FireBlasts(float damage, DamageKind kind, DamageSourceRef src, int count, float range, float area)
    {
        var p = _players[src.PlayerId].Pos;
        for (int b = 0; b < Math.Min(count, 8); b++)
        {
            // Consider every in-range enemy as a center. Equal densities favor the nearer center.
            int best = -1, bestCount = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < _enemyHigh; i++)
            {
                if (!EAlive[i]) continue;
                float distance = Vector2.DistanceSquared(p, new Vector2(EX[i], EY[i]));
                if (distance > range * range) continue;
                var nearby = new CountNear(this, EX[i], EY[i], area * area);
                _grid.Query(EX[i], EY[i], area, ref nearby);
                if (nearby.Count < bestCount || (nearby.Count == bestCount && distance >= bestDistance)) continue;
                (best, bestCount, bestDistance) = (i, nearby.Count, distance);
            }
            if (best < 0) return;
            float cx = EX[best], cy = EY[best];
            var circle = CombatArea.Circle(new Vector2(cx, cy), area);
            for (int i = 0; i < _enemyHigh; i++)
            {
                if (!EAlive[i]) continue;
                float w = circle.WeightAt(new Vector2(EX[i], EY[i]));
                if (w > 0) DamageEnemy(i, damage * (0.5f + 0.5f * w), kind, src, cx, cy, 120);
            }
            Emit(EventKind.Blast, cx, cy, area);
        }
    }
}
