using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const int ProjectileCapacity = 1024;
    private const int HitMemory = 4;

    /// <summary>Bolts splash onto enemies near the impact point, with linear falloff from the centre.</summary>
    public const float SplashRadius = 30f;
    private const float SplashEdgeDamage = 0.5f;

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

    public enum ProjectileStyle : byte { Bolt, Trident, ReturningTrident, Magic, Bottle, Rocket, Phantom }

    public readonly ProjectileStyle[] PStyle = new ProjectileStyle[ProjectileCapacity];

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
            {
                p.HeroAttackTick = Tick;
                p.AttackTimer = 1f / MathF.Max(0.05f, p.Stats[Stat.AttackSpeed] * Rules.Units.AttacksPerUnit);
                ProcAttackItems(p);
                FireIllusion(p);
            }
            else p.AttackTimer = 0; // nothing in range: retry next tick
        }

        // Item weapons: each owned weapon item fires on its own cooldown; extra copies add +1 count.
        for (int i = 0; i < p.ItemStacks.Length; i++)
        {
            if (p.ItemStacks[i] == 0 || Content.Items[i].Weapon is not { } w) continue;
            if (w.Kind is "rocket" or "attackChain") continue;
            if (w.Kind == "cane")
            {
                p.ItemTimers[i] = Math.Max(0, p.ItemTimers[i] - Dt);
                continue;
            }
            var src = new DamageSourceRef(p.Id, i);
            var kind = Kind(w.DamageType);
            int count = w.Count + p.ItemStacks[i] - 1 + (int)p.Stats[Stat.Amount];
            float area = w.Area * p.Stats[Stat.Area];
            float range = w.Range * p.Stats[Stat.Area];
            float cooldown = w.Cooldown * p.Stats[Stat.Cooldown];
            p.ItemTimers[i] -= Dt;
            if (w.Kind == "sustainedCone")
            {
                if (p.ItemTimers[i] <= 0)
                {
                    p.ItemTimers[i] = cooldown;
                    p.ItemPulseTimers[i] = 0;
                }
                if (p.ItemTimers[i] > (w.Cooldown - w.Duration) * p.Stats[Stat.Cooldown] + 0.001f &&
                    (p.ItemPulseTimers[i] -= Dt) <= 0.001f)
                {
                    p.ItemPulseTimers[i] = 0.2f;
                    FireMelee(p, w.Damage * count, kind, src, area, w.Angle, flame: true);
                }
                continue;
            }
            if (p.ItemTimers[i] > 0.001f) continue;
            p.ItemTimers[i] = w.Kind == "aura" ? w.Cooldown : cooldown;
            switch (w.Kind)
            {
                case "bolt": FireBolts(w.Damage, kind, src, count, w.Speed, range, w.Pierce); break;
                case "wood": FireWood(p, w, src, kind, count, area); break;
                case "aura":
                    AreaItemHit(p.Pos, area, w.Damage * count, kind, src, w.Slow);
                    if (Tick % TicksPerSecond < 6) Emit(EventKind.Pulse, p.Pos.X, p.Pos.Y, area);
                    break;
                case "boomerang":
                    for (int k = 0; k < Math.Min(8, count); k++)
                    {
                        float angle = (k - (Math.Min(8, count) - 1) * 0.5f) * 0.24f;
                        SpawnProjectile(p.Pos, Rotate(p.Facing, angle) * w.Speed, w.Damage, kind, src, int.MaxValue,
                            range / w.Speed, ProjectileStyle.Trident);
                    }
                    break;
                case "bounce":
                    for (int k = 0; k < Math.Min(8, count); k++)
                    {
                        int target = RandomEnemy(p.Pos, range);
                        if (target < 0) { p.ItemTimers[i] = 0; break; }
                        LaunchAt(p.Pos, target, w.Damage, w.Speed, range, kind, src, ProjectileStyle.Magic);
                    }
                    break;
                case "dropped":
                    var position = Vector2.Clamp(p.Pos - p.Facing * 12, new Vector2(8), new Vector2(Rules.ArenaSize - 8));
                    SpawnProjectile(position, Vector2.Zero, w.Damage * count, kind, src, 1, w.Duration, ProjectileStyle.Bottle);
                    break;
                case "melee": FireMelee(p, w.Damage * count, kind, src, area, w.Angle); break;
                case "execute":
                    if (!FireSnipe(p, w, kind, src, count, execute: true)) p.ItemTimers[i] = 0;
                    break;
                case "snipe":
                    if (!FireSnipe(p, w, kind, src, count, execute: false)) p.ItemTimers[i] = 0;
                    break;
                case "accelerator":
                    if (p.AcceleratorTicks > 0) { p.ItemTimers[i] = 0; break; }
                    p.AcceleratorTicks = (int)MathF.Ceiling(w.Duration * TicksPerSecond);
                    p.AcceleratorSpeedBonus = w.SpeedBonus;
                    _acceleratorItem[p.Id] = i;
                    for (int enemy = 0; enemy < EnemyCapacity; enemy++) _acceleratorContact[p.Id, enemy] = 0;
                    break;
                case "pulse": FirePulse(w.Damage * Math.Max(1, count), kind, src, area); break;
                case "chain": FireChain(w.Damage, kind, src, count, range, area); break;
                case "blast": FireBlasts(w.Damage, kind, src, count, range, area); break;
            }
        }
    }

    private void ProcAttackItems(PlayerState p)
    {
        for (int i = 0; i < p.ItemStacks.Length; i++)
        {
            if (p.ItemStacks[i] <= 0 || Content.Items[i].Weapon is not { Kind: "rocket" or "attackChain" or "cane" } w)
                continue;
            if (w.Kind == "cane")
            {
                if (p.ItemTimers[i] <= 0.001f && TryCane(p, w, new DamageSourceRef(p.Id, i)))
                    p.ItemTimers[i] = w.Cooldown * p.Stats[Stat.Cooldown];
                continue;
            }
            if (!RollChance(w.Chance * p.ItemStacks[i])) continue;
            var source = new DamageSourceRef(p.Id, i, SuppressProcs: true);
            float area = p.Stats[Stat.Area];
            if (w.Kind == "attackChain")
                FireChain(w.Damage, Kind(w.DamageType), source, w.Count, w.Range * area, w.Area * area);
            else
            {
                int target = Nearest(p.Pos, w.Range * area, []);
                if (target >= 0)
                    LaunchAt(p.Pos, target, p.Stats[Stat.Damage] * w.Damage, w.Speed, w.Range * area,
                        Kind(w.DamageType), source, ProjectileStyle.Rocket);
            }
        }
    }

    private bool TryCane(PlayerState p, ItemWeapon weapon, DamageSourceRef source)
    {
        if (AliveEnemies == 0 || !RollChance(weapon.Chance * p.ItemStacks[source.ItemDef])) return false;
        int target = HighestHealthEnemy();
        Emit(EventKind.ItemBeam, p.Pos.X, p.Pos.Y, x2: EX[target], y2: EY[target]);
        if (Content.Enemies[EDef[target]].Boss) DamageEnemy(target, weapon.Damage, DamageKind.Pure, source, p.Pos.X, p.Pos.Y);
        else ApplyEnemyHit(target, EHp[target], DamageKind.Pure, p.Id, p.Pos.X, p.Pos.Y, 0);
        return true;
    }

    private int HighestHealthEnemy(bool excludeBoss = false)
    {
        int target = -1;
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i] || (excludeBoss && Content.Enemies[EDef[i]].Boss)) continue;
            if (target < 0 || EHp[i] > EHp[target]) target = i;
        }
        return target;
    }

    private void FireWood(PlayerState p, ItemWeapon w, DamageSourceRef source, DamageKind kind, int count, float radius)
    {
        var circle = CombatArea.Circle(p.Pos, radius);
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i] || !circle.Contains(new Vector2(EX[i], EY[i]), out float squared)) continue;
            float weight = Math.Clamp(1 - MathF.Sqrt(squared) / radius, 0, 1);
            ApplyEnemyStatus(i, w.Slow * (0.25f + 0.75f * weight), 2, 0);
            DamageEnemy(i, (w.MinDamage + (w.Damage - w.MinDamage) * weight) * count, kind, source, p.Pos.X, p.Pos.Y);
        }
        Emit(EventKind.Pulse, p.Pos.X, p.Pos.Y, radius);
    }

    private void FireMelee(PlayerState p, float damage, DamageKind kind, DamageSourceRef source, float radius, float angle, bool flame = false)
    {
        var cone = CombatArea.Cone(p.Pos, radius, p.Facing, angle);
        for (int i = 0; i < _enemyHigh; i++)
            if (EAlive[i] && cone.Contains(new Vector2(EX[i], EY[i]), out _))
                DamageEnemy(i, damage, kind, source, p.Pos.X, p.Pos.Y);
        Emit(flame ? EventKind.Flame : EventKind.Melee, p.Pos.X, p.Pos.Y, radius, a: (int)angle, x2: p.Facing.X, y2: p.Facing.Y);
    }

    private bool FireSnipe(PlayerState p, ItemWeapon weapon, DamageKind kind, DamageSourceRef source, int count, bool execute)
    {
        int target = HighestHealthEnemy(excludeBoss: execute);
        if (target < 0) return false;
        Emit(EventKind.ItemBeam, p.Pos.X, p.Pos.Y, x2: EX[target], y2: EY[target]);
        if (execute) ApplyEnemyHit(target, EHp[target], kind, p.Id, p.Pos.X, p.Pos.Y, 0);
        else
        {
            float fraction = Content.Enemies[EDef[target]].Boss ? weapon.BossDamage : weapon.Damage;
            DamageEnemy(target, EHp[target] * fraction * count, kind, source, p.Pos.X, p.Pos.Y);
        }
        return true;
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
    private bool FireBolts(float damage, DamageKind kind, DamageSourceRef src, int count, float speed, float range, int pierce,
        Vector2? origin = null, ProjectileStyle style = ProjectileStyle.Bolt)
    {
        var p = _players[src.PlayerId];
        Vector2 position = origin ?? p.Pos;
        Vector2 muzzle = position + new Vector2(0, -10);
        count = Math.Clamp(count, 1, 16);
        Span<int> targets = stackalloc int[count];
        int n = 0;
        for (; n < count; n++)
        {
            int t = Nearest(position, range, targets[..n]);
            if (t < 0) break;
            targets[n] = t;
        }
        if (n == 0) return false;
        for (int k = 0; k < count; k++)
        {
            int t = targets[k % n];
            var dir = new Vector2(EX[t], EY[t] - 6) - muzzle;
            if (dir.LengthSquared() < 0.01f) dir = p.Facing;
            dir = Vector2.Normalize(dir);
            if (k >= n) dir = Rotate(dir, (k - n + 1) * 0.18f * (k % 2 == 0 ? 1 : -1));
            SpawnProjectile(muzzle, dir * speed, damage, kind, src, pierce, range / speed, style);
        }
        Emit(EventKind.Bolt, muzzle.X, muzzle.Y, n, a: src.ItemDef);
        return true;
    }

    private static Vector2 Rotate(Vector2 v, float a) => new(v.X * MathF.Cos(a) - v.Y * MathF.Sin(a), v.X * MathF.Sin(a) + v.Y * MathF.Cos(a));

    private void LaunchAt(Vector2 from, int target, float damage, float speed, float range,
        DamageKind kind, DamageSourceRef source, ProjectileStyle style)
    {
        var dir = new Vector2(EX[target], EY[target] - 6) - from;
        dir = dir.LengthSquared() > 0.01f ? Vector2.Normalize(dir) : _players[source.PlayerId].Facing;
        SpawnProjectile(from, dir * speed, damage, kind, source, 1, range / speed, style);
    }

    private void SpawnProjectile(Vector2 pos, Vector2 vel, float damage, DamageKind kind, DamageSourceRef src,
        int pierce, float life, ProjectileStyle style = ProjectileStyle.Bolt)
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
        PStyle[slot] = style;
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

    private readonly struct ProjectileSplash(RunWorld w, int proj, CombatArea area, float damage, DamageKind kind, DamageSourceRef src) : Engine.Spatial.IGridVisitor
    {
        public void Visit(int j)
        {
            if (!w.EAlive[j]) return;
            var hits = w._pHits.AsSpan(proj * HitMemory, HitMemory);
            foreach (int uid in hits) if (uid == w.EUid[j]) return;
            float weight = area.WeightAt(new Vector2(w.EX[j], w.EY[j] - 6));
            if (weight <= 0) return;
            w.DamageEnemy(j, damage * (SplashEdgeDamage + (1 - SplashEdgeDamage) * weight), kind, src, w.PX[proj], w.PY[proj], 30);
        }
    }

    private void StepProjectiles()
    {
        for (int i = 0; i < ProjectileCapacity; i++)
        {
            if (!PAlive[i]) continue;
            var style = PStyle[i];
            var source = new DamageSourceRef(_pOwner[i], _pItem[i], SuppressProcs: style is ProjectileStyle.Rocket or ProjectileStyle.Phantom);
            var owner = _players[source.PlayerId];
            var position = new Vector2(PX[i], PY[i]);
            _pLife[i] -= Dt;
            if (style == ProjectileStyle.Bottle)
            {
                if (_pLife[i] <= 0)
                {
                    float radius = Content.Items[source.ItemDef].Weapon!.Area * owner.Stats[Stat.Area];
                    AreaItemHit(position, radius, _pDamage[i], _pKind[i], source);
                    Emit(EventKind.Blast, position.X, position.Y, radius);
                    PAlive[i] = false;
                }
                continue;
            }
            if (style == ProjectileStyle.Trident && _pLife[i] <= 0)
            {
                style = PStyle[i] = ProjectileStyle.ReturningTrident;
                _pLife[i] = 10;
                _pHits.AsSpan(i * HitMemory, HitMemory).Clear();
            }
            if (style == ProjectileStyle.ReturningTrident)
            {
                var delta = owner.Pos - position;
                float speed = Content.Items[source.ItemDef].Weapon!.Speed;
                if (delta.LengthSquared() <= speed * speed * Dt * Dt) { PAlive[i] = false; continue; }
                var velocity = Vector2.Normalize(delta) * speed;
                PVx[i] = velocity.X;
                PVy[i] = velocity.Y;
            }
            if (_pLife[i] <= 0) { PAlive[i] = false; continue; }
            PX[i] += PVx[i] * Dt;
            PY[i] += PVy[i] * Dt;
            var hit = new ProjectileHit(this, i);
            _grid.Query(PX[i], PY[i] + 6, 12, ref hit);
            if (hit.Target < 0) continue;
            int t = hit.Target;
            var hits = _pHits.AsSpan(i * HitMemory, HitMemory);
            for (int k = HitMemory - 1; k > 0; k--) hits[k] = hits[k - 1];
            hits[0] = EUid[t];
            _pPierce[i]--;
            var impact = new Vector2(EX[t], EY[t] - 6);
            DamageEnemy(t, _pDamage[i], _pKind[i], source, PX[i] - PVx[i], PY[i] - PVy[i], 60);
            if (style == ProjectileStyle.Magic && !EAlive[t])
            {
                var weapon = Content.Items[source.ItemDef].Weapon!;
                float range = weapon.Range * owner.Stats[Stat.Area];
                int next = Nearest(impact, range, []);
                if (next >= 0)
                    LaunchAt(impact, next, _pDamage[i], weapon.Speed, range, _pKind[i], source, style);
            }
            if (style == ProjectileStyle.Bolt)
            {
                var splash = new ProjectileSplash(this, i, CombatArea.Circle(new Vector2(PX[i], PY[i]), SplashRadius), _pDamage[i], _pKind[i], source);
                _grid.Query(PX[i], PY[i] + 6, SplashRadius, ref splash);
            }
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
        hops = Math.Clamp(hops, 1, MaxChainHops);
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
