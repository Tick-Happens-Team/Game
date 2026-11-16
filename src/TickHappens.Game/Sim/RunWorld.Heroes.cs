using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    internal const int HeroIllusionCapacity = 8;
    internal const int BoarChargeTicks = 5 * TicksPerSecond;
    private const int TrailInterval = 6;
    internal const int HeroTrailSeconds = 5;
    internal const int TrailCapacity = HeroTrailSeconds * TicksPerSecond / TrailInterval;
    private const float IllusionCooldown = 16;
    private const float TrailRadius = 14;
    private readonly (Vector2 Position, int Until)[,] _heroTrail = new (Vector2, int)[4, TrailCapacity];

    private static readonly Stat[] CubeStats =
        [Stat.MaxHp, Stat.Regen, Stat.MoveSpeed, Stat.AttackSpeed, Stat.Armor, Stat.MagicResist];

    internal static int CubeStatCount => CubeStats.Length;

    internal static readonly CompanionDef[] SoulCompanions =
    [
        new() { Kind = "soulHealer", Sprite = "companion_soul_healer", Speed = 120 },
        new() { Kind = "soulWarrior", Sprite = "companion_soul_warrior", Speed = 90, Range = 200, AttackInterval = 1 },
        new() { Kind = "soulWraith", Sprite = "companion_soul_wraith", Speed = 80, Range = 220, AttackInterval = 1.4f, Area = 45 },
    ];

    private static float PuppeteerBonus(PlayerState player) =>
        player.Character.Id == "puppeteer" ? 0.1f + 0.01f * (player.Level - 1) : 0;

    private static void ApplyHeroStats(PlayerState player)
    {
        int level = player.Level - 1;
        switch (player.Character.Id)
        {
            case "masochist":
                float missing = 1 - Math.Clamp(player.Hp / Math.Max(1, MaxHealthFor(player)), 0, 1);
                player.Stats.ApplyDynamic(Stat.Regen, missing * (10 + 0.01f * level), 0);
                player.Stats.ApplyDynamic(Stat.MoveSpeed, missing * (50 + 0.03f * level), 0);
                break;
            case "goflin":
                player.Stats.ApplyDynamic(Stat.Damage, player.Wallet.Gold * 0.1f, 0);
                break;
            case "diver":
                player.Stats.ApplyDynamic(Stat.Armor, MathF.Floor(player.HeroDistance / 100) * 0.01f, 0);
                break;
            case "cybernetic_cube":
                for (int i = 0; i < CubeStats.Length; i++)
                    player.Stats.ApplyDynamic(CubeStats[i], 0, player.CubeBonuses[i]);
                break;
        }
    }

    private void RollCubeLevel(PlayerState player)
    {
        if (player.Character.Id != "cybernetic_cube") return;
        int stat = _rng.Int(CubeStats.Length);
        player.CubeBonuses[stat] += _rng.Range(0.0075f, 0.0825f);
    }

    private void OnHeroDamaged(PlayerState player, float damage)
    {
        if (player.Character.Id == "miner")
        {
            player.PendingBlastDamage += damage;
        }
        else if (player.Character.Id == "pig" && player.Hp <= 0)
        {
            player.Hp = 1;
            player.GhostUntilTick = Tick + (int)MathF.Round((5 + 0.2f * (player.Level - 1)) * TicksPerSecond);
            Emit(EventKind.Pulse, player.Pos.X, player.Pos.Y, 24);
        }
    }

    private void StepBoarContact(PlayerState player, int enemy, bool touching)
    {
        ref int contact = ref _boarContact[player.Id, enemy];
        if (!touching)
        {
            contact = 0;
            return;
        }
        if (contact == EUid[enemy]) return;
        contact = EUid[enemy];
        bool charged = player.BoarWalkTicks == BoarChargeTicks;
        float damage = MaxHealthFor(player) * (charged ? 0.055f : 0.05f);
        DamageEnemy(enemy, damage, DamageKind.Physical, new DamageSourceRef(player.Id),
            player.Pos.X, player.Pos.Y, knock: 0);
        if (!EAlive[enemy]) return;

        var offset = new Vector2(EX[enemy], EY[enemy]) - player.Pos;
        var direction = offset.LengthSquared() > 0.0001f ? Vector2.Normalize(offset) : player.Facing;
        float impulse = charged || player.GhostUntilTick > Tick ? 145 : 85;
        if (Content.Enemies[EDef[enemy]].Boss) impulse *= 0.35f;
        EKx[enemy] += direction.X * impulse;
        EKy[enemy] += direction.Y * impulse;
    }

    private void StepHeroPassive(PlayerState player)
    {
        int level = player.Level - 1;
        if (player.TimekeeperSlowed && player.NextHeroPassiveTick > Tick && Tick % 4 == 0)
            player.NextHeroPassiveTick++;
        switch (player.Character.Id)
        {
            case "miner":
                while (player.PendingBlastDamage >= 80)
                {
                    player.PendingBlastDamage -= 80;
                    float radius = 50 + 2.25f * level;
                    AreaItemHit(player.Pos, radius, 15 + 2.5f * level, DamageKind.Physical,
                        new DamageSourceRef(player.Id, SuppressProcs: true));
                    Emit(EventKind.Blast, player.Pos.X, player.Pos.Y, radius);
                }
                break;
            case "skater":
                if (Tick % TrailInterval == 0) StepFireTrail(player, 5 + 2.75f * level, TrailRadius + 0.5f * level);
                break;
            case "necromancer":
                if (Tick >= player.NextHeroPassiveTick)
                {
                    int souls = player.Souls;
                    int kind = souls <= 20 ? 0 : souls <= 50 ? 1 : 2;
                    player.NextHeroPassiveTick = Tick + 30 * TicksPerSecond;
                    if (souls > 0 && SpawnCompanion(player.Id, -1 - kind, player.Pos - player.Facing * 16, souls))
                    {
                        player.Souls = 0;
                        Emit(EventKind.Pulse, player.Pos.X, player.Pos.Y, 24);
                    }
                }
                break;
            case "illusionist":
                if (Tick >= player.NextHeroPassiveTick)
                {
                    player.NextHeroPassiveTick = Tick + Math.Max(1, (int)MathF.Round((IllusionCooldown - 0.05f * level) * TicksPerSecond));
                    SpawnHeroIllusion(player, level);
                }
                break;
        }
    }

    private void StepFireTrail(PlayerState player, float damagePerSecond, float radius)
    {
        int slot = Tick / TrailInterval % TrailCapacity;
        if (player.Walking)
        {
            _heroTrail[player.Id, slot] = (player.Prev, Tick + HeroTrailSeconds * TicksPerSecond);
            Emit(EventKind.HeroFireTrail, player.Prev.X, player.Prev.Y, a: player.Id);
        }
        float damage = damagePerSecond * TrailInterval * Dt;
        var source = new DamageSourceRef(player.Id, SuppressProcs: true);
        for (int enemy = 0; enemy < _enemyHigh; enemy++)
        {
            if (!EAlive[enemy]) continue;
            var pos = new Vector2(EX[enemy], EY[enemy]);
            for (int j = 0; j < TrailCapacity; j++)
            {
                var patch = _heroTrail[player.Id, j];
                if (patch.Until <= Tick || Vector2.DistanceSquared(pos, patch.Position) > radius * radius) continue;
                DamageEnemy(enemy, damage, DamageKind.Magical, source, patch.Position.X, patch.Position.Y, 0);
                break;
            }
        }
    }

    private Vector2 HealWithSoul(ref Companion soul, PlayerState owner)
    {
        int targetId = NearestLivingPlayer(soul.Pos);
        var target = targetId >= 0 ? _players[targetId] : owner;
        Vector2 goal = target.Pos;
        float distance = Vector2.DistanceSquared(goal, soul.Pos);
        int companion = -1;
        for (int i = 0; i < _companions.Length; i++)
        {
            ref readonly var ally = ref _companions[i];
            if (!ally.Active || ally.Item == -1 || ally.Health <= 0) continue;
            float d = Vector2.DistanceSquared(ally.Pos, soul.Pos);
            if (d >= distance) continue;
            (goal, distance, companion) = (ally.Pos, d, i);
        }
        if (distance > 8 * 8) return goal;
        if (companion >= 0) RestoreCompanion(ref _companions[companion], soul.MaxHealth);
        else RestorePlayer(target, soul.MaxHealth);
        Emit(EventKind.Pulse, goal.X, goal.Y, 16);
        soul.Active = false;
        return goal;
    }

    private void SpawnHeroIllusion(PlayerState player, int levelsGained)
    {
        var illusions = player.Illusions;
        int slot = 0;
        for (int i = 1; i < illusions.Length && illusions[slot].UntilTick > Tick; i++)
            if (illusions[i].UntilTick < illusions[slot].UntilTick) slot = i;
        var position = Vector2.Clamp(player.Pos - player.Facing * 22,
            new Vector2(12), new Vector2(Rules.ArenaSize - 12));
        float health = 0.5f * MaxHealthFor(player);
        illusions[slot] = new HeroIllusion
        {
            Position = position,
            UntilTick = Tick + (int)MathF.Round((8 + 0.15f * levelsGained) * TicksPerSecond),
            Health = health,
            MaxHealth = health
        };
        Emit(EventKind.Pulse, position.X, position.Y, 20);
    }

    private Vector2 EnemyTarget(Vector2 enemy, PlayerState player, out int decoy)
    {
        var closest = player.Pos;
        float distance = Vector2.DistanceSquared(closest, enemy);
        decoy = -1;
        foreach (var ally in _players)
        {
            if (!ally.Active || ally.Hp <= 0 || ally.Character.Id != "illusionist") continue;
            for (int i = 0; i < ally.Illusions.Length; i++)
            {
                ref readonly var illusion = ref ally.Illusions[i];
                if (illusion.UntilTick <= Tick) continue;
                float d = Vector2.DistanceSquared(illusion.Position, enemy);
                if (d >= distance) continue;
                (closest, distance, decoy) = (illusion.Position, d, ally.Id * HeroIllusionCapacity + i);
            }
        }
        return closest;
    }
}
