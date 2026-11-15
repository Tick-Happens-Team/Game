using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public struct Companion
{
    public bool Active, FacingLeft;
    public int Id, Owner, Item;
    public Vector2 Pos, Prev;
    internal float Health, MaxHealth, AuraCapacity, Shield, AttackTimer, Life;
    internal int Target, TargetUid, HurtTick;
    internal float DamageBonus, AttackSpeedBonus;
    internal float Armor, BlockChance, AttackSpeed, PhysicalDamage, Lifesteal, DamageReduction;
}

public sealed partial class RunWorld
{
    public const int CompanionCapacity = 64;
    private const float CompanionRadius = 6;
    private readonly Companion[] _companions = new Companion[CompanionCapacity];
    private int _nextCompanionId = 1;

    public ReadOnlySpan<Companion> Companions => _companions;

    private void StepCompanionItems(PlayerState player)
    {
        for (int item = 0; item < player.ItemStacks.Length; item++)
        {
            if (player.ItemStacks[item] <= 0 || Content.Items[item].Companion is not { } def ||
                def.Kind == "demon") continue;
            player.ItemTimers[item] -= Dt;
            if (player.ItemTimers[item] > 0) continue;
            int count = 0;
            foreach (ref readonly var ally in Companions)
                if (ally.Active && ally.Owner == player.Id && ally.Item == item) count++;
            if (count >= Math.Min(8, def.Kind is "gnome" or "boar" ? player.ItemStacks[item] * 3 : player.ItemStacks[item]))
                continue;
            if (SpawnCompanion(player.Id, item, player.Pos - player.Facing * 16, def.Health))
                player.ItemTimers[item] = def.Interval * player.Stats[Stat.Cooldown];
        }
    }

    private CompanionDef GetCompanionDef(int item) =>
        item < 0 ? SoulCompanions[-1 - item] : Content.Items[item].Companion!;

    private bool SpawnCompanion(int owner, int item, Vector2 position, float health)
    {
        for (int i = 0; i < CompanionCapacity; i++)
        {
            if (_companions[i].Active) continue;
            var def = GetCompanionDef(item);
            position = Vector2.Clamp(position, new Vector2(8), new Vector2(Rules.ArenaSize - 8));
            _companions[i] = new Companion
            {
                Active = true, Id = _nextCompanionId++, Owner = owner, Item = item, Pos = position, Prev = position,
                Life = def.Duration, Health = health, MaxHealth = health, AuraCapacity = health, Target = -1, HurtTick = -100, AttackTimer = def.Kind == "eagle" ? def.Interval : 0,
            };
            return true;
        }
        return false;
    }

    private void StepCompanions()
    {
        for (int i = 0; i < CompanionCapacity; i++)
        {
            ref var ally = ref _companions[i];
            if (!ally.Active) continue;
            var owner = _players[ally.Owner];
            var def = GetCompanionDef(ally.Item);
            if (!owner.Active || owner.Hp <= 0 || (def.Duration > 0 && (ally.Life -= Dt) <= 0))
            {
                ally.Active = false;
                continue;
            }
            ally.Prev = ally.Pos;
            UpdateCompanionAuras(ref ally);
            ally.AttackTimer -= Dt;
            Vector2 goal = owner.Pos + new Vector2(MathF.Cos(Tick * Dt * 2 + i) * 20, MathF.Sin(Tick * Dt * 2 + i) * 14);
            if (def.Kind == "soulHealer")
                goal = HealWithSoul(ref ally, owner);
            else if (def.Kind == "leech")
            {
                float drain = Math.Min(def.Damage * Dt, ally.Health);
                RestorePlayer(owner, drain);
                ally.Health -= drain;
                if (ally.Health <= 0.001f) ally.Active = false;
            }
            else if (def.Kind == "dog")
                goal = FetchXp(ally, owner, def.Range * owner.Stats[Stat.Area], goal);
            else
                goal = StepAttackingCompanion(i, ref ally, def, owner, goal);
            if (!ally.Active) continue;
            goal = Vector2.Clamp(goal, new Vector2(8), new Vector2(Rules.ArenaSize - 8));
            Vector2 delta = goal - ally.Pos;
            float distance = delta.Length();
            if (distance < 0.01f) continue;
            ally.FacingLeft = delta.X < 0;
            ally.Pos += delta * (Math.Min(distance, def.Speed * Dt) / distance);
        }
    }

    private Vector2 FetchXp(in Companion ally, PlayerState owner, float range, Vector2 fallback)
    {
        int target = -1;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < PickupCapacity; i++)
        {
            if (!GAlive[i] || GKind[i] != PickupKind.Xp || _gPulled[i]) continue;
            var pos = new Vector2(GX[i], GY[i]);
            if (Vector2.DistanceSquared(pos, owner.Pos) > range * range) continue;
            float d = Vector2.DistanceSquared(ally.Pos, pos);
            if (d >= closest) continue;
            closest = d;
            target = i;
        }
        if (target < 0) return fallback;
        if (closest < 8 * 8)
        {
            _gPulled[target] = true;
            _gPullTarget[target] = (byte)owner.Id;
            return fallback;
        }
        return new Vector2(GX[target], GY[target]);
    }

    private Vector2 StepAttackingCompanion(int slot, ref Companion ally, CompanionDef def, PlayerState owner, Vector2 fallback)
    {
        int target = ally.Target;
        if (def.Kind == "eagle")
        {
            if (target >= 0 && (!EAlive[target] || EUid[target] != ally.TargetUid)) target = -1;
            if (target < 0 && ally.AttackTimer <= 0)
            {
                target = RandomEnemy(owner.Pos, def.Range * owner.Stats[Stat.Area]);
                if (target >= 0)
                {
                    ally.TargetUid = EUid[target];
                    ally.AttackTimer = def.Interval * owner.Stats[Stat.Cooldown];
                }
            }
            ally.Target = target;
        }
        else target = Nearest(ally.Pos, def.Range * owner.Stats[Stat.Area], []);
        if (target < 0) return fallback;

        var pos = new Vector2(EX[target], EY[target]);
        float range = def.Kind == "boar" ? 85 : 12;
        if (Vector2.DistanceSquared(ally.Pos, pos) > range * range) return pos;
        if (ally.AttackTimer > 0 && def.Kind != "eagle") return ally.Pos;

        var source = new DamageSourceRef(owner.Id, ally.Item, SuppressProcs: true, CompanionSlot: slot);
        float damage = ally.Item < 0 ? ally.MaxHealth : def.Damage;
        var kind = Kind(def.DamageType);
        if (def.Area > 0)
        {
            float radius = def.Area * owner.Stats[Stat.Area];
            AreaItemHit(pos, radius, damage, kind, source, def.Slow, def.Stun);
            var effect = def.Kind switch
            {
                "drone" => EventKind.DroneBlast,
                "eagle" => EventKind.EagleDive,
                "boar" => EventKind.BoarStomp,
                _ => EventKind.Pulse,
            };
            Emit(effect, pos.X, pos.Y, radius, x2: ally.Pos.X, y2: ally.Pos.Y);
        }
        else DamageEnemy(target, damage, kind, source, ally.Pos.X, ally.Pos.Y);
        if (def.Kind == "drone")
        {
            ally.Active = false;
            owner.ItemTimers[ally.Item] = def.Interval * owner.Stats[Stat.Cooldown];
        }
        else if (def.Kind == "eagle") ally.Target = -1;
        else ally.AttackTimer = owner.Stats[Stat.Cooldown] /
            ((1 / Math.Max(0.05f, def.AttackInterval) + ally.AttackSpeed * Rules.Units.AttacksPerUnit) * (1 + ally.AttackSpeedBonus));
        return ally.Pos;
    }

    private int RandomEnemy(Vector2 origin, float range, bool excludeBoss = false)
    {
        int result = -1, count = 0;
        float squared = range * range;
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i] || (excludeBoss && Content.Enemies[EDef[i]].Boss) || Vector2.DistanceSquared(origin, new Vector2(EX[i], EY[i])) > squared) continue;
            if (_rng.Int(++count) == 0) result = i;
        }
        return result;
    }

    private void AreaItemHit(Vector2 center, float radius, float damage, DamageKind kind, DamageSourceRef source,
        float slow = 0, float stun = 0)
    {
        var circle = CombatArea.Circle(center, radius);
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i] || !circle.Contains(new Vector2(EX[i], EY[i]), out _)) continue;
            ApplyEnemyStatus(i, slow, slow > 0 ? 2 : 0, stun);
            DamageEnemy(i, damage, kind, source, center.X, center.Y, 0);
        }
    }

    private bool TryHitCompanion(Vector2 enemy, float radius, float damage, DamageKind kind)
    {
        for (int i = 0; i < CompanionCapacity; i++)
        {
            ref var ally = ref _companions[i];
            if (!ally.Active || !_players[ally.Owner].Active || ally.Health <= 0 || Vector2.DistanceSquared(enemy, ally.Pos) >= radius * radius) continue;
            var def = GetCompanionDef(ally.Item);
            if (def.Kind is "leech" or "eagle" or "drone" or "dog" or "soulHealer") continue;
            if (Tick - ally.HurtTick < TicksPerSecond / 2 || kind == DamageKind.Physical && RollChance(ally.BlockChance))
                return true;
            ally.HurtTick = Tick;
            damage *= (1 - Resist(kind, ally.Armor, 0)) * MathF.Max(0.1f, 1 - ally.DamageReduction);
            float absorbed = Math.Min(damage, ally.Shield);
            ally.Shield -= absorbed;
            ally.Health -= damage - absorbed;
            if (ally.Health <= 0) ally.Active = false;
            return true;
        }
        return false;
    }
}
