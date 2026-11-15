using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const int EnemyWindupTicks = 8;
    private const int CloudPulseTicks = TicksPerSecond / 5;

    public record struct EnemyShot(Vector2 Pos, Vector2 Velocity, int Life, float Damage, DamageKind Kind);
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
                _enemyAttackTicks[slot] = EnemyWindupTicks;
            return;
        }
        if (--_enemyAttackTicks[slot] > 0) return;

        if (def.DetonateRange > 0)
        {
            Kill(slot, credit: -1);
            return;
        }

        float attacksPerSecond = def.AttackSpeed * Rules.Units.AttacksPerUnit;
        _enemyAttackReady[slot] = Tick + Math.Max(1, (int)MathF.Round(TicksPerSecond / attacksPerSecond) - EnemyWindupTicks);
        if (distance > range) return;
        var kind = _enemyKind[EDef[slot]];
        if (def.ShotRange > 0)
        {
            if (_enemyShotCount == _enemyShots.Length) return;
            var direction = distance > 0.01f ? aim / distance : Vector2.UnitX;
            var velocity = direction * def.ShotSpeed;
            int life = (int)MathF.Ceiling((def.ShotRange + PlayerRadius) / (def.ShotSpeed * Dt));
            _enemyShots[_enemyShotCount++] = new EnemyShot(pos, velocity, life, EDamage[slot], kind);
            return;
        }

        HitPlayerWithEnemy(slot, def, target, decoy, lifestealCap);
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

        float damage = HurtPlayer(target, EDamage[slot], _enemyKind[EDef[slot]], EX[slot], EY[slot], slot);
        if (!EAlive[slot] || damage <= 0) return;
        if (def.Lifesteal > 0)
            EHp[slot] = MathF.Min(EMaxHp[slot], EHp[slot] + damage * Math.Min(lifestealCap, def.Lifesteal));
        float reflected = damage * target.Stats[Stat.DamageReflection];
        if (reflected > 0) ApplyEnemyHit(slot, reflected, DamageKind.Pure, target.Id, target.Pos.X, target.Pos.Y, 0);
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
            if (hit != null) HurtPlayer(hit, shot.Damage, shot.Kind, previous.X, previous.Y, -1);
            if (--shot.Life <= 0 || hit != null || shot.Pos.X < 0 || shot.Pos.Y < 0 ||
                shot.Pos.X > Rules.ArenaSize || shot.Pos.Y > Rules.ArenaSize)
                shot = _enemyShots[--_enemyShotCount];
        }
    }
}
