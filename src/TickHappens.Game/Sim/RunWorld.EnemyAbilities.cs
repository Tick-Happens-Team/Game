using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private struct EnemyAbility
    {
        public int Ready, SecondaryReady, Expires, Link, LinkUid, Hits;
        public int ParentUid, Group, SpeedBuffs;
        public float HealthAtCast, Progress;
        public Vector2 Point, Destination;
    }

    private readonly EnemyAbility[] _enemyAbilities;
    private readonly bool[] _enemyHidden;
    private readonly int _phantomIllusion, _goldVein, _juniorCultist, _seniorCultist, _cultistShaman;
    private int _timekeepersAlive, _timekeeperTicks, _guardiansAlive;
    internal const float TimekeeperAuraRadius = 250f / 3;

    private void InitEnemyAbility(int slot, EnemyDef def)
    {
        _enemyHidden[slot] = false;
        _enemyAbilities[slot] = new EnemyAbility
        {
            Ready = Tick + (def.Behavior switch
            {
                "devourer" => 20 * TicksPerSecond,
                "prospector" or "junior" or "ancient" => 15 * TicksPerSecond,
                "senior" => 13 * TicksPerSecond,
                "phantom" => 4 * TicksPerSecond,
                _ => 0,
            }),
            SecondaryReady = Tick + (def.Behavior switch
            {
                "phantom" => 30 * TicksPerSecond,
                "senior" => 25 * TicksPerSecond,
                _ => 0,
            }),
        };
        if (def.Behavior == "timekeeper") _timekeepersAlive++;
        if (def.Behavior == "guardian") _guardiansAlive++;
    }

    private void OnEnemyAbilityDeath(int slot, EnemyDef def)
    {
        switch (def.Behavior)
        {
            case "guardian": _guardiansAlive--; break;
            case "timekeeper": _timekeepersAlive--; break;
            case "phantom": RemovePhantomGroup(EUid[slot], 0); break;
            case "prospector" when EnemyLinkAlive(_enemyAbilities[slot]):
                Kill(_enemyAbilities[slot].Link, credit: -1, deathEffects: false);
                break;
            case "thief":
                foreach (var p in _players)
                    if (p.StolenBy == EUid[slot]) RestoreStolenItem(p);
                break;
        }
    }

    private Vector2 EnemySteering(int slot, EnemyDef def, PlayerState player, Vector2 enemy, Vector2 target, bool decoy)
    {
        // Stable assignments spread melee units across the two sides of the player's projected path.
        if (def.Boss || def.ShotRange > 0 || def.DetonateRange > 0 || def.Speed <= 0 ||
            decoy || EUid[slot] % 3 != 0) return target;
        var motion = player.Pos - player.Prev;
        float distance = Vector2.Distance(enemy, target);
        if (motion.LengthSquared() < 0.04f || distance < 40 || distance > 550) return target;
        var forward = Vector2.Normalize(motion);
        if (Vector2.Dot(enemy - target, forward) > 80) return target;
        float lead = Math.Clamp(distance / (def.Speed * Rules.Units.MoveSpeedScale), 0.45f, 1.5f);
        float side = EUid[slot] % 2 == 0 ? 1 : -1;
        var lateral = new Vector2(-forward.Y, forward.X) * (side * Math.Clamp(distance * 0.3f, 20, 80));
        var projected = target + motion * (lead / Dt) + lateral;
        return Vector2.Clamp(projected, new Vector2(12), new Vector2(Rules.ArenaSize - 12));
    }

    public bool EnemyHidden(int slot) => _enemyHidden[slot];

    private float ItemStep(PlayerState player) => player.TimekeeperSlowed ? Dt * 0.75f : Dt;

    private bool InTimekeeperAura(PlayerState player)
    {
        if (_timekeepersAlive == 0 && _replicaTick < 0) return false;
        for (int i = 0; i < _enemyHigh; i++)
            if (EAlive[i] && Content.Enemies[EDef[i]].Behavior == "timekeeper" &&
                Vector2.DistanceSquared(player.Pos, new(EX[i], EY[i])) <= TimekeeperAuraRadius * TimekeeperAuraRadius)
                return true;
        return false;
    }

    private static bool IsCultist(EnemyDef def) => def.Behavior is "junior" or "senior" or "ancient" or "shaman";

    private int FindEnemy(Vector2 pos, int source, float radius, string? behavior = null, bool cultist = false)
    {
        int result = -1;
        float closest = radius * radius;
        for (int j = 0; j < _enemyHigh; j++)
        {
            if (j == source || !EAlive[j]) continue;
            var def = Content.Enemies[EDef[j]];
            if (behavior != null ? def.Behavior != behavior :
                cultist ? !IsCultist(def) : def.Boss || def.Behavior is "illusion" or "vein") continue;
            float d = Vector2.DistanceSquared(pos, new(EX[j], EY[j]));
            if (d >= closest) continue;
            result = j;
            closest = d;
        }
        return result;
    }

    private bool AbilityReady(ref int ready, float delay)
    {
        if (Tick < ready) return false;
        ready = Tick + (int)(delay * TicksPerSecond);
        return true;
    }

    private Vector2 EnemyPointAround(Vector2 origin, float min, float max)
    {
        float angle = _rng.Range(0, MathF.Tau);
        var point = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * _rng.Range(min, max);
        return Vector2.Clamp(point, new Vector2(12), new Vector2(Rules.ArenaSize - 12));
    }

    private void HealEnemy(int slot, float amount) => EHp[slot] = Math.Min(EMaxHp[slot], EHp[slot] + amount);

    private void StepEnemyAbility(int slot, EnemyDef def, PlayerState player, Vector2 target, ref Vector2 goal, ref float speed)
    {
        ref var state = ref _enemyAbilities[slot];
        var pos = new Vector2(EX[slot], EY[slot]);
        float range = Vector2.Distance(pos, target);
        switch (def.Behavior)
        {
            case "jumper":
                if (AbilityReady(ref state.Ready, 2))
                {
                    var direction = target - pos;
                    state.Destination = pos + direction * (Math.Min(range, 83.333f) / Math.Max(1, range));
                    state.Expires = Tick + 10;
                }
                if (Tick <= state.Expires)
                {
                    goal = state.Destination;
                    speed = Vector2.Distance(pos, goal) / ((state.Expires - Tick + 1) * Dt);
                }
                break;
            case "siphoner":
                if (range < 135 && AbilityReady(ref state.Ready, 0.2f))
                {
                    float drain = HurtPlayer(player, 1, DamageKind.Magical, pos.X, pos.Y, slot, continuous: true);
                    EMaxHp[slot] += drain;
                    HealEnemy(slot, drain);
                    if (drain > 0) Emit(EventKind.ItemBeam, player.Pos.X, player.Pos.Y, x2: pos.X, y2: pos.Y);
                }
                if (range < 125) speed *= range < 70 ? -1 : 0;
                break;
            case "screamer":
                if (range < 190) speed *= range < 100 ? -1 : 0;
                if (state.Expires == 0 && state.Ready != int.MaxValue && range < 235)
                {
                    state.Expires = Tick + 10 * TicksPerSecond;
                    Emit(EventKind.Pulse, pos.X, pos.Y, 36);
                }
                if (state.Expires > 0 && Tick >= state.Expires)
                {
                    state.Expires = 0;
                    state.Ready = int.MaxValue;
                    for (int j = 0; j < _enemyHigh; j++)
                    {
                        if (!EAlive[j] || Content.Enemies[EDef[j]].Behavior is "vein" or "illusion") continue;
                        EMaxHp[j] += 50;
                        EHp[j] += 50;
                        _enemyAbilities[j].SpeedBuffs++;
                    }
                    Emit(EventKind.Pulse, pos.X, pos.Y, 65);
                }
                break;
            case "teleporter":
                if (AbilityReady(ref state.Ready, 4)) MoveEnemyInstantly(slot, EnemyPointAround(target, 80, 145));
                break;
            case "ninja":
                _enemyHidden[slot] = Tick >= state.SecondaryReady && range > 24;
                if (Tick < state.SecondaryReady)
                {
                    goal = pos + (pos - target);
                    break;
                }
                if (range > 60) goal = target - player.Facing * 18;
                break;
            case "thief":
                if (range <= 175 && Tick >= state.Ready && TryStealItem(player, slot))
                    state.Ready = Tick + 15 * TicksPerSecond;
                break;
            case "devourer":
                if (EHp[slot] < EMaxHp[slot] * 0.25f) speed *= 1.5f;
                if (AbilityReady(ref state.Ready, 20))
                {
                    int meal = FindEnemy(pos, slot, float.PositiveInfinity);
                    if (meal >= 0)
                    {
                        Kill(meal, credit: -1, deathEffects: false);
                        HealEnemy(slot, 300);
                        Emit(EventKind.Pulse, pos.X, pos.Y, 28);
                    }
                }
                break;
            case "phantom":
                StepPhantom(slot, target, ref state);
                break;
            case "prospector":
                StepProspector(slot, pos, ref state, ref goal, ref speed);
                break;
            case "vein":
                goal = pos;
                break;
            case "junior":
                if (AbilityReady(ref state.Ready, 15))
                {
                    int ally = FindEnemy(pos, slot, 100, cultist: true);
                    if (ally >= 0)
                    {
                        state.Point = new Vector2(EX[ally], EY[ally]);
                        state.Expires = Tick + 5 * TicksPerSecond;
                        Emit(EventKind.Pulse, state.Point.X, state.Point.Y, 28);
                    }
                }
                if (state.Expires > Tick) HealCultists(state.Point, 28, 10 * Dt);
                break;
            case "senior":
                if (AbilityReady(ref state.Ready, 13))
                {
                    state.Point = player.Pos;
                    state.Expires = Tick + 2 * TicksPerSecond;
                    Emit(EventKind.Pulse, state.Point.X, state.Point.Y, 100);
                }
                if (state.Expires > 0 && Tick >= state.Expires)
                {
                    state.Expires = 0;
                    EnemyAreaAttack(state.Point, 100, 50, DamageKind.Magical, mark: true);
                }
                if (AbilityReady(ref state.SecondaryReady, 25))
                {
                    SummonEnemies(_juniorCultist, pos, 3);
                    SummonEnemies(_cultistShaman, pos, 1);
                }
                break;
            case "ancient":
                StepAncientCultist(slot, pos, ref state);
                break;
            case "shaman":
                if (range <= 185 && AbilityReady(ref state.Ready, 12))
                {
                    state.Point = pos;
                    AddBloodRain(pos);
                    EHp[slot] -= 50;
                    if (EHp[slot] <= 0) Kill(slot, credit: -1);
                }
                break;
        }
    }

    private void MoveEnemyInstantly(int slot, Vector2 position)
    {
        EX[slot] = _prevX[slot] = position.X;
        EY[slot] = _prevY[slot] = position.Y;
        EKx[slot] = EKy[slot] = 0;
    }

    private void StepPhantom(int slot, Vector2 target, ref EnemyAbility state)
    {
        if (_phantomIllusion < 0) return;
        if (state.Group > 0 && state.HealthAtCast > 0 && state.HealthAtCast - EHp[slot] >= 500)
        {
            RemovePhantomGroup(EUid[slot], state.Group);
            state.Expires = Tick + 5 * TicksPerSecond;
            state.HealthAtCast = 0;
            ApplyEnemyStatus(slot, 0, 0, 5);
            return;
        }
        if (state.Expires > Tick) return;
        if (AbilityReady(ref state.SecondaryReady, 30))
        {
            RemovePhantomGroup(EUid[slot], 0);
            state.Group++;
            state.HealthAtCast = EHp[slot];
            state.Expires = 0;
            MoveEnemyInstantly(slot, EnemyPointAround(target, 60, 140));
            for (int j = 0; j < 3; j++) SpawnIllusion(slot, EnemyPointAround(target, 60, 140), state.Group, 0);
            state.Ready = Tick + 4 * TicksPerSecond;
        }
        if (state.HealthAtCast <= 0 && AbilityReady(ref state.Ready, 4))
        {
            var previous = new Vector2(EX[slot], EY[slot]);
            MoveEnemyInstantly(slot, EnemyPointAround(target, 60, 233.333f));
            SpawnIllusion(slot, previous, 0, Tick + 5 * TicksPerSecond);
        }
    }

    private void SpawnIllusion(int parent, Vector2 pos, int group, int expires)
    {
        int clone = SpawnEnemy(_phantomIllusion, pos);
        if (clone < 0) return;
        EDamage[clone] = EDamage[parent] * 0.3f;
        _enemyAbilities[clone].ParentUid = EUid[parent];
        _enemyAbilities[clone].Group = group;
        _enemyAbilities[clone].Expires = expires;
    }

    private void RemovePhantomGroup(int uid, int group)
    {
        for (int j = 0; j < _enemyHigh; j++)
            if (EAlive[j] && _enemyAbilities[j].ParentUid == uid && (group == 0 || _enemyAbilities[j].Group == group))
                Kill(j, credit: -1, deathEffects: false);
    }

    private bool EnemyLinkAlive(in EnemyAbility state) =>
        state.LinkUid != 0 && EAlive[state.Link] && EUid[state.Link] == state.LinkUid;

    private void StepProspector(int slot, Vector2 pos, ref EnemyAbility state, ref Vector2 goal, ref float speed)
    {
        bool fever = EHp[slot] < EMaxHp[slot] * 0.35f;
        if (fever) speed *= 2;
        if (_goldVein < 0) return;
        if (!EnemyLinkAlive(state) && AbilityReady(ref state.Ready, 15))
        {
            int vein = SpawnEnemy(_goldVein, EnemyPointAround(pos, 70, 333.333f));
            if (vein >= 0) (state.Link, state.LinkUid, state.Progress) = (vein, EUid[vein], 0);
        }
        if (!EnemyLinkAlive(state)) return;
        goal = new Vector2(EX[state.Link], EY[state.Link]);
        if (Vector2.DistanceSquared(pos, goal) > 22 * 22) return;
        speed = 0;
        state.Progress += Dt;
        if (state.Progress < (fever ? 2 : 3)) return;
        Kill(state.Link, credit: -1, deathEffects: false);
        HealEnemy(slot, 750);
        Emit(EventKind.Pulse, pos.X, pos.Y, 30);
    }

    private void StepAncientCultist(int slot, Vector2 pos, ref EnemyAbility state)
    {
        if (AbilityReady(ref state.Ready, 15))
        {
            SummonEnemies(_seniorCultist, pos, 1);
            SummonEnemies(_juniorCultist, pos, 3);
        }
        if (state.LinkUid != 0)
        {
            if (!EnemyLinkAlive(state)) state.LinkUid = 0;
            else if (Tick >= state.Expires)
            {
                Kill(state.Link, credit: -1, deathEffects: false);
                HealEnemy(slot, 500);
                state.Group = 1;
                state.LinkUid = 0;
            }
        }
        if (state.Group == 0 && state.LinkUid == 0 && EHp[slot] < EMaxHp[slot] * 0.5f &&
            AbilityReady(ref state.SecondaryReady, 15))
        {
            int sacrifice = FindEnemy(pos, slot, 220, "senior");
            if (sacrifice >= 0)
            {
                (state.Link, state.LinkUid, state.Expires) = (sacrifice, EUid[sacrifice], Tick + 3 * TicksPerSecond);
                ApplyEnemyStatus(sacrifice, 0, 0, 3);
                Emit(EventKind.Pulse, EX[sacrifice], EY[sacrifice], 34);
            }
        }
    }

    private void SummonEnemies(int def, Vector2 pos, int count)
    {
        if (def < 0) return;
        for (int j = 0; j < count && AliveEnemies < Content.Waves.MaxAlive; j++)
            SpawnEnemy(def, EnemyPointAround(pos, 12, 26));
    }

    private void EnemyAreaAttack(Vector2 center, float radius, float damage, DamageKind kind, bool mark = false,
        EventKind effect = EventKind.EnemyBlast)
    {
        Emit(effect, center.X, center.Y, radius);
        foreach (var player in _players)
        {
            if (!player.Active || Vector2.DistanceSquared(player.Pos, center) > radius * radius) continue;
            float hit = HurtPlayer(player, damage, kind, center.X, center.Y, -1);
            if (mark && hit > 0) player.CultistMarkUntil = Tick + 8 * TicksPerSecond;
        }
    }

    private readonly (Vector2 Point, int Until)[] _bloodRain = new (Vector2, int)[32];

    private void AddBloodRain(Vector2 center)
    {
        int slot = 0;
        for (int i = 1; i < _bloodRain.Length; i++)
            if (_bloodRain[i].Until < _bloodRain[slot].Until) slot = i;
        _bloodRain[slot] = (center, Tick + 5 * TicksPerSecond);
        Emit(EventKind.Pulse, center.X, center.Y, 200);
    }

    private void StepBloodRain()
    {
        if (Tick % 5 != 0) return;
        foreach (var rain in _bloodRain)
            if (rain.Until > Tick) HealCultists(rain.Point, 200, 35 * 5 * Dt);
    }

    private void HealCultists(Vector2 center, float radius, float amount)
    {
        for (int j = 0; j < _enemyHigh; j++)
            if (EAlive[j] && IsCultist(Content.Enemies[EDef[j]]) &&
                Vector2.DistanceSquared(center, new(EX[j], EY[j])) <= radius * radius)
                HealEnemy(j, amount);
    }
}
