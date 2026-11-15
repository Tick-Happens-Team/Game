using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const int EnemyWindupTicks = 8;
    public const int KamikazeFuseTicks = TicksPerSecond;
    private const int CloudPulseTicks = TicksPerSecond / 5;

    public record struct EnemyShot(Vector2 Pos, Vector2 Velocity, int Life, float Damage, DamageKind Kind, int Source = -1, int SourceUid = 0);
    public record struct EnemyCloud(Vector2 Pos, float Radius, int Life, float Dps);
    private readonly record struct EnemyBlast(Vector2 Pos, float Radius, float Damage, DamageKind Kind, int Credit);

    private readonly EnemyShot[] _enemyShots = new EnemyShot[256];
    private readonly EnemyCloud[] _enemyClouds = new EnemyCloud[64];
    private readonly Queue<EnemyBlast> _enemyBlasts;
    private readonly byte[] _enemyAttackTicks;
    private readonly int[] _enemyAttackReady;
    private int _enemyShotCount, _enemyCloudCount;

    public ReadOnlySpan<EnemyShot> EnemyShots => _enemyShots.AsSpan(0, _enemyShotCount);
    public ReadOnlySpan<EnemyCloud> EnemyClouds => _enemyClouds.AsSpan(0, _enemyCloudCount);
    public int EnemyWindup(int slot) => _enemyAttackTicks[slot];

    public static int EnemyWindupDuration(EnemyDef def) => def.DetonateRange > 0 ? KamikazeFuseTicks : EnemyWindupTicks;

    private void StepEnemyAttack(int slot, EnemyDef def, PlayerState target, PlayerState? decoy, Vector2 targetPosition, bool stunned, float lifestealCap)
    {
        if (stunned)
        {
            _enemyAttackTicks[slot] = 0;
            return;
        }

        var pos = new Vector2(EX[slot], EY[slot]);
        var aim = targetPosition - pos;
        float distance = aim.Length();
        float range = def.DetonateRange > 0 ? def.DetonateRange :
            def.ShotRange > 0 ? def.ShotRange : def.Radius + PlayerRadius;
        if (_enemyAttackTicks[slot] == 0)
        {
            if (Tick >= _enemyAttackReady[slot] && distance <= range)
                _enemyAttackTicks[slot] = (byte)EnemyWindupDuration(def);
            return;
        }
        if (--_enemyAttackTicks[slot] > 0) return;

        if (def.DetonateRange > 0)
        {
            Kill(slot, credit: -1);
            return;
        }

        float attackSpeed = def.AttackSpeed;
        if (def.Behavior == "prospector" && EHp[slot] < EMaxHp[slot] * 0.35f) attackSpeed = 40;
        if (def.Behavior == "ancient" && _enemyAbilities[slot].Group > 0) attackSpeed += 20;
        if (IsCultist(def) && target.CultistMarkUntil > Tick) attackSpeed *= 1.15f;
        float attacksPerSecond = attackSpeed * Rules.Units.AttacksPerUnit;
        _enemyAttackReady[slot] = Tick + Math.Max(1, (int)MathF.Round(TicksPerSecond / attacksPerSecond) - EnemyWindupTicks);
        if (distance > range) return;
        var kind = _enemyKind[EDef[slot]];
        if (def.Behavior == "witch" && decoy == null)
        {
            var from = pos;
            foreach (var player in _players)
            {
                if (!player.Active || player.Hp <= 0 || (player != target && Vector2.DistanceSquared(player.Pos, target.Pos) > 90 * 90)) continue;
                Emit(EventKind.ItemBeam, from.X, from.Y, x2: player.Pos.X, y2: player.Pos.Y);
                HealFromEnemyHit(slot, def, HurtPlayer(player, EDamage[slot], kind, from.X, from.Y, slot), lifestealCap);
                from = player.Pos;
            }
            return;
        }
        if (def.ShotRange > 0)
        {
            if (def.Behavior == "ancient")
            {
                Vector2 lateral = distance > 0.01f ? new Vector2(-aim.Y, aim.X) / distance : Vector2.UnitY;
                FireEnemyShot(slot, def, pos - lateral * 25, targetPosition, kind);
                FireEnemyShot(slot, def, pos + lateral * 25, targetPosition, kind);
            }
            else FireEnemyShot(slot, def, pos, targetPosition, kind);
            return;
        }

        HitPlayerWithEnemy(slot, def, target, decoy, lifestealCap);
    }

    private void FireEnemyShot(int slot, EnemyDef def, Vector2 origin, Vector2 target, DamageKind kind)
    {
        if (_enemyShotCount == _enemyShots.Length) return;
        var aim = target - origin;
        float distance = aim.Length();
        var direction = distance > 0.01f ? aim / distance : Vector2.UnitX;
        var velocity = direction * def.ShotSpeed;
        int life = (int)MathF.Ceiling((Math.Max(def.ShotRange, distance) + PlayerRadius) / (def.ShotSpeed * Dt));
        _enemyShots[_enemyShotCount++] = new EnemyShot(origin, velocity, life, EDamage[slot], kind, slot, EUid[slot]);
    }

    private void HitPlayerWithEnemy(int slot, EnemyDef def, PlayerState target, PlayerState? decoy, float lifestealCap)
    {
        if (decoy != null)
        {
            if (Tick >= decoy.IllusionHurtTick)
            {
                decoy.IllusionHurtTick = Tick + TicksPerSecond / 2;
                decoy.IllusionHealth -= EDamage[slot];
                if (decoy.IllusionHealth <= 0) decoy.IllusionUntilTick = 0;
            }
            return;
        }

        bool critical = def.Behavior == "sheWolf" && RollChance(0.33f);
        float damage = HurtPlayer(target, EDamage[slot] * (critical ? 2 : 1), _enemyKind[EDef[slot]], EX[slot], EY[slot], slot);
        if (!EAlive[slot] || damage <= 0) return;
        HealFromEnemyHit(slot, def, damage, lifestealCap);
        if (def.Behavior == "ninja")
        {
            _enemyHidden[slot] = false;
            _enemyAbilities[slot].SecondaryReady = Tick + 10 * TicksPerSecond;
            target.EnemySlowUntil = Tick + 2 * TicksPerSecond;
        }
        if (def.Behavior == "sheWolf" && ++_enemyAbilities[slot].Hits % 3 == 0)
            target.EnemyRootUntil = Tick + 2 * TicksPerSecond;
        float reflected = damage * target.Stats[Stat.DamageReflection];
        if (reflected > 0) ApplyEnemyHit(slot, reflected, DamageKind.Pure, target.Id, target.Pos.X, target.Pos.Y, 0);
    }

    private void HealFromEnemyHit(int slot, EnemyDef def, float damage, float cap)
    {
        float lifesteal = def.Lifesteal + (def.Behavior == "ancient" && _enemyAbilities[slot].Group > 0 ? 0.15f : 0);
        if (EAlive[slot] && lifesteal > 0 && damage > 0) HealEnemy(slot, damage * Math.Min(cap, lifesteal));
    }

    private void QueueEnemyDeathEffect(int slot, EnemyDef def, int credit)
    {
        var pos = new Vector2(EX[slot], EY[slot]);
        if (def.CloudRadius > 0)
        {
            int index = _enemyCloudCount;
            if (index == _enemyClouds.Length)
            {
                index = 0;
                for (int i = 1; i < _enemyCloudCount; i++)
                    if (_enemyClouds[i].Life < _enemyClouds[index].Life) index = i;
            }
            else _enemyCloudCount++;
            int life = (int)MathF.Ceiling(def.CloudSeconds * TicksPerSecond / CloudPulseTicks) * CloudPulseTicks;
            _enemyClouds[index] = new EnemyCloud(pos, def.CloudRadius, life, def.CloudDps);
        }
        if (def.BlastRadius > 0)
            _enemyBlasts.Enqueue(new EnemyBlast(pos, def.BlastRadius, EDamage[slot], _enemyKind[EDef[slot]], credit));
    }

    private void StepEnemyEffects()
    {
        // Deaths append to this queue so chain reactions never recurse through the damage pipeline.
        while (_enemyBlasts.TryDequeue(out var blast))
        {
            float radiusSquared = blast.Radius * blast.Radius;
            Emit(EventKind.EnemyBlast, blast.Pos.X, blast.Pos.Y, blast.Radius);
            foreach (var player in _players)
                if (player.Active && Vector2.DistanceSquared(player.Pos, blast.Pos) <= radiusSquared)
                    HurtPlayer(player, blast.Damage, blast.Kind, blast.Pos.X, blast.Pos.Y, -1);
            for (int slot = 0; slot < _enemyHigh; slot++)
            {
                if (!EAlive[slot] || Vector2.DistanceSquared(new Vector2(EX[slot], EY[slot]), blast.Pos) > radiusSquared) continue;
                var def = Content.Enemies[EDef[slot]];
                float damage = blast.Damage * (1 - Resist(blast.Kind, def.Armor, def.MagicResist));
                ApplyEnemyHit(slot, damage, blast.Kind, blast.Credit, blast.Pos.X, blast.Pos.Y, 65, triggerItems: false);
            }
        }
        for (int i = _enemyCloudCount - 1; i >= 0; i--)
        {
            ref var cloud = ref _enemyClouds[i];
            if (cloud.Life % CloudPulseTicks == 0)
            {
                foreach (var player in _players)
                    if (player.Active && Vector2.DistanceSquared(player.Pos, cloud.Pos) <= cloud.Radius * cloud.Radius)
                        HurtPlayer(player, cloud.Dps * CloudPulseTicks * Dt, DamageKind.Magical, cloud.Pos.X, cloud.Pos.Y, -1, continuous: true);
            }
            if (--cloud.Life <= 0) cloud = _enemyClouds[--_enemyCloudCount];
        }

        for (int i = _enemyShotCount - 1; i >= 0; i--)
        {
            ref var shot = ref _enemyShots[i];
            var previous = shot.Pos;
            var travel = shot.Velocity * Dt;
            shot.Pos += travel;
            PlayerState? hit = null;
            float first = float.PositiveInfinity;
            foreach (var player in _players)
            {
                if (!player.Active || player.Hp <= 0) continue;
                float t = Math.Clamp(Vector2.Dot(player.Pos - previous, travel) / travel.LengthSquared(), 0, 1);
                float radius = PlayerRadius + 2;
                if (t >= first || Vector2.DistanceSquared(player.Pos, previous + travel * t) > radius * radius) continue;
                first = t;
                hit = player;
            }
            if (hit != null)
            {
                int source = shot.Source;
                bool sourceAlive = source >= 0 && EAlive[source] && EUid[source] == shot.SourceUid;
                float damage = HurtPlayer(hit, shot.Damage, shot.Kind, previous.X, previous.Y, sourceAlive ? source : -1);
                if (sourceAlive && damage > 0)
                {
                    float cap = float.PositiveInfinity;
                    foreach (var player in _players)
                        if (player.Active && player.Hp > 0 && player.Stats[Stat.EnemyLifestealCap] > 0) cap = 0.2f;
                    HealFromEnemyHit(source, Content.Enemies[EDef[source]], damage, cap);
                }
            }
            if (--shot.Life <= 0 || hit != null || shot.Pos.X < 0 || shot.Pos.Y < 0 ||
                shot.Pos.X > Rules.ArenaSize || shot.Pos.Y > Rules.ArenaSize)
                shot = _enemyShots[--_enemyShotCount];
        }
    }
}
