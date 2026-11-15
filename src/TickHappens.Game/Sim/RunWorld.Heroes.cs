using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private const int TrailInterval = 6;
    private const int TrailCapacity = 3 * TicksPerSecond / TrailInterval;
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
                float regenGrowth = missing * 0.15f * level / (1 + missing);
                player.Stats.ApplyDynamic(Stat.Regen, regenGrowth, missing);
                player.Stats.ApplyDynamic(Stat.MoveSpeed, 0, missing * (1 + 0.003f * level));
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
        player.CubeBonuses[stat] += _rng.Range(0.01f, 0.10f);
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
            player.GhostUntilTick = Tick + (int)MathF.Round((5 + 0.25f * (player.Level - 1)) * TicksPerSecond);
            Emit(EventKind.Pulse, player.Pos.X, player.Pos.Y, 24);
        }
    }

    private void StepHeroPassive(PlayerState player)
    {
        int level = player.Level - 1;
        if (player.TimekeeperSlowed && player.NextHeroPassiveTick > Tick && Tick % 4 == 0)
            player.NextHeroPassiveTick++;
        switch (player.Character.Id)
        {
            case "miner":
                while (player.PendingBlastDamage >= 50)
                {
                    player.PendingBlastDamage -= 50;
                    float radius = 50 + 2.5f * level;
                    AreaItemHit(player.Pos, radius, 5 + 2.5f * level, DamageKind.Physical,
                        new DamageSourceRef(player.Id, SuppressProcs: true));
                    Emit(EventKind.Blast, player.Pos.X, player.Pos.Y, radius);
                }
                break;
            case "skater":
                if (Tick % TrailInterval == 0) StepFireTrail(player, 5 + 3 * level);
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
                    player.NextHeroPassiveTick = Tick + 15 * TicksPerSecond;
                    player.IllusionPosition = Vector2.Clamp(player.Pos - player.Facing * 22,
                        new Vector2(12), new Vector2(Rules.ArenaSize - 12));
                    player.IllusionUntilTick = Tick + 8 * TicksPerSecond;
                    player.IllusionHealth = player.IllusionMaxHealth = 0.3f * MaxHealthFor(player);
                    Emit(EventKind.Pulse, player.IllusionPosition.X, player.IllusionPosition.Y, 20);
                }
                break;
        }
    }

    private void StepFireTrail(PlayerState player, float damagePerSecond)
    {
        int slot = Tick / TrailInterval % TrailCapacity;
        if (player.Walking)
        {
            _heroTrail[player.Id, slot] = (player.Prev, Tick + 3 * TicksPerSecond);
            Emit(EventKind.HeroFireTrail, player.Prev.X, player.Prev.Y);
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
                if (patch.Until <= Tick || Vector2.DistanceSquared(pos, patch.Position) > TrailRadius * TrailRadius) continue;
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

    private Vector2 EnemyTarget(Vector2 enemy, PlayerState player, out PlayerState? decoy)
    {
        var closest = player.Pos;
        float distance = Vector2.DistanceSquared(closest, enemy);
        decoy = null;
        foreach (var ally in _players)
        {
            if (!ally.Active || ally.Hp <= 0 || ally.IllusionUntilTick <= Tick || ally.IllusionHealth <= 0) continue;
            float d = Vector2.DistanceSquared(ally.IllusionPosition, enemy);
            if (d >= distance) continue;
            (closest, distance, decoy) = (ally.IllusionPosition, d, ally);
        }
        return closest;
    }
}
