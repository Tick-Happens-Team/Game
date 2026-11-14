using System.Numerics;

namespace TickHappens.Game;

public readonly record struct RunInput(int X, int Y);

public sealed class RunWorld
{
    public const int TicksPerSecond = 30;
    public const int ArenaSize = 1024;
    private const float StepDistance = 90f / TicksPerSecond;
    private const float Reach = 22;
    private const float AttackRange = 36;
    private const int LocalPlayerId = 0;

    private static readonly Vector2[] ChestPositions =
    [
        new(512, 462),
        new(430, 550),
        new(614, 553),
    ];
    private readonly bool[] _opened = new bool[ChestPositions.Length];
    private static readonly Vector2[] EnemyPositions =
        [new(510, 485), new(453, 540), new(414, 526), new(574, 546), new(601, 576)];
    private readonly float[] _enemyHealth = [15, 15, 15, 15, 15];
    private readonly GoldDrop?[] _drops = new GoldDrop?[EnemyPositions.Length];
    private int _nextAttackTick;

    public ChestWallet Wallet { get; } = new();
    public CombatStats Stats { get; set; } = CombatStats.Base;
    public int Tick { get; private set; }
    public Vector2 Hero { get; private set; } = new(512, 512);
    public Vector2 PreviousHero { get; private set; } = new(512, 512);
    public bool Walking { get; private set; }
    public bool FacingLeft { get; private set; }
    public Vector2 Facing { get; private set; } = Vector2.UnitY;
    public int OpenedCount { get; private set; }
    public int NearbyChest { get; private set; } = -1;
    public int LastOpenedChest { get; private set; } = -1;
    public int LastOpenedTick { get; private set; }
    public ReadOnlySpan<Vector2> Chests => ChestPositions;
    public ReadOnlySpan<Vector2> Enemies => EnemyPositions;
    public bool IsOpen(int chest) => _opened[chest];
    public bool IsAlive(int enemy) => _enemyHealth[enemy] > 0;
    public bool HasGoldDrop(int enemy) => _drops[enemy].HasValue;

    public void Step(RunInput input)
    {
        Tick++;
        PreviousHero = Hero;
        var direction = new Vector2(Math.Sign(input.X), Math.Sign(input.Y));
        if (direction != Vector2.Zero)
        {
            Facing = Vector2.Normalize(direction);
            Hero = Vector2.Clamp(Hero + Facing * StepDistance, new(12), new(ArenaSize - 12));
        }
        Walking = Hero != PreviousHero;
        if (direction.X != 0) FacingLeft = direction.X < 0;

        AttackNearestEnemy();
        var pickupArea = CombatArea.Circle(Hero, 16);
        for (int i = 0; i < _drops.Length; i++)
        {
            if (_drops[i] is not { } drop || drop.OwnerId != LocalPlayerId ||
                !pickupArea.Contains(EnemyPositions[i], out _)) continue;
            Wallet.Collect(drop, LocalPlayerId);
            _drops[i] = null;
        }

        NearbyChest = -1;
        var chestArea = CombatArea.Circle(Hero, Reach);
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < ChestPositions.Length; i++)
        {
            if (_opened[i]) continue;
            if (!chestArea.Contains(ChestPositions[i], out float distance) || distance >= bestDistance) continue;
            bestDistance = distance;
            NearbyChest = i;
        }

        if (NearbyChest >= 0 && Wallet.TryPay())
        {
            _opened[NearbyChest] = true;
            LastOpenedChest = NearbyChest;
            LastOpenedTick = Tick;
            NearbyChest = -1;
            OpenedCount++;
        }
    }

    private void AttackNearestEnemy()
    {
        if (Tick < _nextAttackTick) return;
        int target = -1;
        var area = CombatArea.Circle(Hero, Stats.EffectRadius(AttackRange));
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < EnemyPositions.Length; i++)
        {
            if (!IsAlive(i) || !area.Contains(EnemyPositions[i], out float distance) || distance >= nearest) continue;
            nearest = distance;
            target = i;
        }
        if (target < 0) return;

        ApplyDamage(target, new(new(LocalPlayerId), DamageType.Physical, Stats.OutgoingDamage(15, DamageType.Physical)));
        _nextAttackTick = Tick + TicksPerSecond / 2;
    }

    public void ApplyDamage(int target, DamageHit hit)
    {
        if (!float.IsFinite(hit.Amount) || hit.Amount < 0) throw new ArgumentOutOfRangeException(nameof(hit));
        if (!IsAlive(target)) return;
        _enemyHealth[target] = Math.Max(0, _enemyHealth[target] - hit.Amount);
        if (!IsAlive(target)) _drops[target] = new(10, hit.Source.CreditPlayerId);
    }
}
