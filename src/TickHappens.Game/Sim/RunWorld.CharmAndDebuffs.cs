using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private readonly float[,] _armorShred;
    private readonly int[,] _armorShredUntil;
    private readonly int[] _charmOwner, _charmUntil, _charmAttackReady;

    public bool IsCharmed(int enemy) => _charmUntil[enemy] > Tick;

    internal float ArmorShredAt(int enemy)
    {
        float reduction = 0;
        for (int owner = 0; owner < _players.Length; owner++)
            if (_armorShredUntil[owner, enemy] > Tick) reduction += _armorShred[owner, enemy];
        return reduction;
    }

    private bool HasNegativeEffect(int enemy)
    {
        if (_enemySlowUntil[enemy] > Tick || _enemyStunUntil[enemy] > Tick || ArmorShredAt(enemy) > 0)
            return true;
        for (int owner = 0; owner < _players.Length; owner++)
            if (_poisonTick[owner, enemy] > Tick) return true;
        return false;
    }

    private float DebuffedDamageMultiplier(PlayerState player)
    {
        float bonus = 0;
        foreach (int item in player.EffectItems)
            if (ItemEnabled(player, item) && Content.Items[item].Effect is { Kind: "debuffedDamage" } effect)
                bonus = Math.Max(bonus, effect.Value);
        return 1 + bonus;
    }

    private void ApplyAttackModifiers(PlayerState player, int enemy)
    {
        float duration = 3 * player.Stats[Stat.DebuffDuration];
        float shred = player.Stats[Stat.ArmorShred];
        if (shred > 0)
        {
            _armorShred[player.Id, enemy] = shred;
            _armorShredUntil[player.Id, enemy] = Tick + (int)MathF.Ceiling(duration * TicksPerSecond);
        }
        ApplyEnemyStatus(enemy, Math.Min(0.9f, player.Stats[Stat.HitSlow]), duration, 0);
    }

    private void VacuumEnemies(PlayerState player, ItemEffect effect)
    {
        float radius = effect.Radius * player.Stats[Stat.Area];
        for (int enemy = 0; enemy < _enemyHigh; enemy++)
        {
            if (!EAlive[enemy] || IsCharmed(enemy)) continue;
            var position = new Vector2(EX[enemy], EY[enemy]);
            if (Vector2.DistanceSquared(position, player.Pos) > radius * radius) continue;
            // Pull to a ring so large crowds cannot be placed inside the hero.
            var offset = position - player.Pos;
            float distance = offset.Length();
            if (distance > PlayerRadius + Content.Enemies[EDef[enemy]].Radius)
            {
                var destination = player.Pos + offset / distance * (PlayerRadius + Content.Enemies[EDef[enemy]].Radius);
                (EX[enemy], EY[enemy]) = (destination.X, destination.Y);
            }
            EKx[enemy] = EKy[enemy] = 0;
            ApplyEnemyStatus(enemy, 0, 0, effect.Secondary * player.Stats[Stat.DebuffDuration]);
        }
        BuildEnemyGrid();
        Emit(EventKind.Pulse, player.Pos.X, player.Pos.Y, radius);
    }

    private void CharmNearestEnemy(PlayerState player, float radius, float seconds)
    {
        int target = -1;
        float best = radius * radius;
        for (int enemy = 0; enemy < _enemyHigh; enemy++)
        {
            if (!EAlive[enemy] || EElite[enemy] || IsCharmed(enemy)) continue;
            var def = Content.Enemies[EDef[enemy]];
            if (def.Boss || def.Elite || def.Behavior is "vein" or "illusion") continue;
            float distance = Vector2.DistanceSquared(player.Pos, new(EX[enemy], EY[enemy]));
            if (distance > best) continue;
            (best, target) = (distance, enemy);
        }
        if (target < 0) return;
        _charmOwner[target] = player.Id;
        _charmUntil[target] = Tick + (int)MathF.Ceiling(seconds * TicksPerSecond);
        _charmAttackReady[target] = Tick;
        _enemyAttackTicks[target] = 0;
        Emit(EventKind.Pulse, EX[target], EY[target], 18);
    }

    private bool StepCharmedEnemy(int enemy)
    {
        if (!IsCharmed(enemy)) return false;
        var pos = new Vector2(EX[enemy], EY[enemy]);
        int target = -1;
        float best = float.PositiveInfinity;
        for (int other = 0; other < _enemyHigh; other++)
        {
            if (other == enemy || !EAlive[other] || IsCharmed(other) ||
                Content.Enemies[EDef[other]].Behavior == "vein") continue;
            float distance = Vector2.DistanceSquared(pos, new(EX[other], EY[other]));
            if (distance < best) (best, target) = (distance, other);
        }
        if (target < 0 || _enemyStunUntil[enemy] > Tick) return true;
        var def = Content.Enemies[EDef[enemy]];
        float reach = def.ShotRange > 0 ? def.ShotRange : def.Radius + Content.Enemies[EDef[target]].Radius + 2;
        float distanceToTarget = MathF.Sqrt(best);
        if (distanceToTarget > reach)
        {
            float speed = Math.Max(def.Speed, 10) * Rules.Units.MoveSpeedScale;
            if (_enemySlowUntil[enemy] > Tick) speed *= 1 - _enemySlow[enemy];
            pos += (new Vector2(EX[target], EY[target]) - pos) / distanceToTarget *
                Math.Min(distanceToTarget - reach, speed * Dt);
            (EX[enemy], EY[enemy]) = (pos.X, pos.Y);
        }
        else if (Tick >= _charmAttackReady[enemy])
        {
            float attacks = def.AttackSpeed > 0 ? def.AttackSpeed * Rules.Units.AttacksPerUnit : 1;
            _charmAttackReady[enemy] = Tick + Math.Max(1, (int)MathF.Round(TicksPerSecond / attacks));
            var targetDef = Content.Enemies[EDef[target]];
            var kind = Kind(def.DamageType);
            float damage = EDamage[enemy] * 0.7f * (1 - Resist(kind, targetDef.Armor, targetDef.MagicResist));
            ApplyEnemyHit(target, damage, kind, _charmOwner[enemy], pos.X, pos.Y, 20, triggerItems: false);
        }
        return true;
    }
}
