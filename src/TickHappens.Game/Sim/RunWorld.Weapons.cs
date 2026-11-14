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
    public readonly bool[] PAlive = new bool[ProjectileCapacity];
    private readonly int[] _pHits = new int[ProjectileCapacity * HitMemory];
    private int _projCursor;

    public WeaponLevel LevelOf(WeaponState w) => Content.Weapons[w.Def].Levels[w.Level - 1];

    private void StepWeapons()
    {
        var p = Player;
        foreach (var w in p.Weapons)
        {
            w.Timer -= Dt;
            if (w.Timer > 0) continue;
            var def = Content.Weapons[w.Def];
            var lv = LevelOf(w);
            w.Timer = lv.Cooldown * p.Stats[Stat.Cooldown];
            float damage = lv.Damage * p.Stats[Stat.Might];
            int extra = (int)p.Stats[Stat.Amount];
            float area = lv.Area * p.Stats[Stat.Area];
            switch (def.Kind)
            {
                case "bolt": FireBolts(lv, damage, lv.Count + extra); break;
                case "pulse": FirePulse(damage, area); break;
                case "chain": FireChain(lv, damage, lv.Count + extra, area); break;
                case "blast": FireBlasts(lv, damage, lv.Count + extra, area); break;
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

    private void FireBolts(WeaponLevel lv, float damage, int count)
    {
        var p = Player;
        Span<int> targets = stackalloc int[Math.Min(count, 16)];
        int n = 0;
        for (; n < targets.Length; n++)
        {
            int t = Nearest(p.Pos, lv.Range, targets[..n]);
            if (t < 0) break;
            targets[n] = t;
        }
        if (n == 0) return;
        for (int k = 0; k < Math.Min(count, 16); k++)
        {
            int t = targets[k % n];
            var dir = new Vector2(EX[t], EY[t]) - p.Pos;
            if (dir.LengthSquared() < 0.01f) dir = p.Facing;
            dir = Vector2.Normalize(dir);
            if (k >= n) dir = Rotate(dir, (k - n + 1) * 0.18f * (k % 2 == 0 ? 1 : -1));
            SpawnProjectile(p.Pos + new Vector2(0, -10), dir * lv.Speed, damage, lv.Pierce, lv.Range / lv.Speed);
        }
        Emit(EventKind.Bolt, p.Pos.X, p.Pos.Y - 10, n);
    }

    private static Vector2 Rotate(Vector2 v, float a) => new(v.X * MathF.Cos(a) - v.Y * MathF.Sin(a), v.X * MathF.Sin(a) + v.Y * MathF.Cos(a));

    private void SpawnProjectile(Vector2 pos, Vector2 vel, float damage, int pierce, float life)
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
            DamageEnemy(t, _pDamage[i], Player.Id, PX[i] - PVx[i], PY[i] - PVy[i], 60);
            if (_pPierce[i] <= 0) PAlive[i] = false;
        }
    }

    private void FirePulse(float damage, float area)
    {
        var p = Player.Pos;
        var circle = CombatArea.Circle(p, area);
        for (int i = 0; i < _enemyHigh; i++)
            if (EAlive[i] && circle.Contains(new Vector2(EX[i], EY[i]), out _))
                DamageEnemy(i, damage, Player.Id, p.X, p.Y, 90);
        Emit(EventKind.Pulse, p.X, p.Y, area);
    }

    private void FireChain(WeaponLevel lv, float damage, int hops, float hopRange)
    {
        hops = Math.Min(hops, 16);
        Span<int> chain = stackalloc int[hops];
        int n = 0;
        var from = Player.Pos;
        for (; n < hops; n++)
        {
            int t = Nearest(from, n == 0 ? lv.Range : hopRange, chain[..n]);
            if (t < 0) break;
            chain[n] = t;
            from = new Vector2(EX[t], EY[t]);
        }
        if (n == 0) return;
        int start = ChainPoints.Count;
        ChainPoints.Add(Player.Pos + new Vector2(0, -14));
        for (int k = 0; k < n; k++) ChainPoints.Add(new Vector2(EX[chain[k]], EY[chain[k]] - 6));
        Emit(EventKind.Chain, Player.Pos.X, Player.Pos.Y, n, a: start, b: n + 1);
        for (int k = 0; k < n; k++) DamageEnemy(chain[k], damage, Player.Id, EX[chain[k]], EY[chain[k]] - 1, 10);
    }

    private struct CountNear(RunWorld w, float x, float y, float r) : Engine.Spatial.IGridVisitor
    {
        public int Count;
        public void Visit(int j)
        {
            if (!w.EAlive[j]) return;
            float dx = w.EX[j] - x, dy = w.EY[j] - y;
            if (dx * dx + dy * dy <= r * r) Count++;
        }
    }

    private void FireBlasts(WeaponLevel lv, float damage, int count, float area)
    {
        var p = Player.Pos;
        for (int b = 0; b < Math.Min(count, 8); b++)
        {
            // Sample a few candidates in range and pick the densest spot.
            int best = -1, bestCount = 0;
            for (int s = 0; s < 8; s++)
            {
                int i = SampleAliveNear(p, lv.Range);
                if (i < 0) break;
                var c = new CountNear(this, EX[i], EY[i], area);
                _grid.Query(EX[i], EY[i], area, ref c);
                if (c.Count > bestCount) { bestCount = c.Count; best = i; }
            }
            if (best < 0) return;
            float cx = EX[best], cy = EY[best];
            var circle = CombatArea.Circle(new Vector2(cx, cy), area);
            for (int i = 0; i < _enemyHigh; i++)
            {
                if (!EAlive[i]) continue;
                float w = circle.WeightAt(new Vector2(EX[i], EY[i]));
                if (w > 0) DamageEnemy(i, damage * (0.5f + 0.5f * w), Player.Id, cx, cy, 120);
            }
            Emit(EventKind.Blast, cx, cy, area);
        }
    }

    private int SampleAliveNear(Vector2 p, float range)
    {
        if (AliveEnemies == 0) return -1;
        for (int tries = 0; tries < 24; tries++)
        {
            int i = _rng.Int(Math.Max(1, _enemyHigh));
            if (!EAlive[i]) continue;
            float dx = EX[i] - p.X, dy = EY[i] - p.Y;
            if (dx * dx + dy * dy <= range * range) return i;
        }
        return Nearest(p, range, []);
    }
}
