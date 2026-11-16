using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

/// <summary>Per-tick player input. Movement, dash and chest choices are manual actions.</summary>
public readonly record struct RunInput(int X, int Y, bool Dash = false, int ChestChoice = 0, bool OpenChest = false);

public enum RunPhase { Playing, Dead }

public enum EventKind : byte
{
    Hit, Kill, Bolt, Pulse, Chain, Blast, LevelUp, Statue, Chest, PlayerHurt, Dash, Pickup, Elite, Melee, ItemBeam, Flame, Frost, BossReward,
    DroneBlast, HeroFireTrail, EnemyBlast,
    EagleDive, BoarStomp, RocketImpact, WoodShockwave, WildAura, Laser, Rifle, CaneStrike, Revival, EnemyQuake, EnemyTeleport,
}

public enum BlastStyle { Default, SodaSplash }

/// <summary>Gameplay event for cosmetics (VFX, sound, damage numbers). Never drives simulation.</summary>
public struct GameEvent
{
    public EventKind Kind;
    public float X, Y, X2, Y2, Value;
    /// <summary>Chain: start index into <see cref="RunWorld.ChainPoints"/>; Hit: damage kind; Chest: item index.
    /// ItemBeam: zero for a lightning arc, or the drained player ID plus one (B is then the siphoner slot and Value its network UID).</summary>
    public int A, B;
    /// <summary>Gold drop RNG state for a kill. Cosmetic seed for other events.</summary>
    public uint Seed;
}

/// <summary>Small deterministic generator (xorshift32). Same seed, same sequence, on every machine.</summary>
public struct SimRng(uint seed)
{
    private uint _s = seed == 0 ? 0x9E3779B9u : seed;

    internal uint State => _s;

    public uint NextU()
    {
        uint x = _s;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _s = x;
    }

    public float Next() => (NextU() >> 8) * (1f / 16777216f);
    public float Range(float a, float b) => a + (b - a) * Next();
    public int Int(int n) => (int)(NextU() % (uint)n);
}

/// <summary>Identifies who dealt damage: the basic attack or an item (for item-damage bonuses and credit).</summary>
public readonly record struct DamageSourceRef(int PlayerId, int ItemDef = -1, bool SuppressProcs = false, int CompanionSlot = -1)
{
    public bool FromItem => ItemDef >= 0;
}

public sealed class PlayerState
{
    public int Id;
    public bool Active;
    public int NearbyChest = -1;
    public int LastChestItem = -1;
    public int ChestOfferFirst = -1, ChestOfferSecond = -1, ChestOfferThird = -1;
    public int ChestOfferUntilTick;
    public Vector2 Pos, Prev, Facing = Vector2.UnitY;
    public bool FacingLeft, Walking;
    public float Hp;
    public int RevivalTicks;
    public int Level = 1;
    public float Xp, XpNext;
    public required StatBlock Stats;
    public required ChestWallet Wallet;
    public required CharacterDef Character;
    public required AttackDef Attack;
    /// <summary>Copies owned per item definition.</summary>
    public required int[] ItemStacks;
    internal int EnemySlowUntil, EnemyRootUntil, CultistMarkUntil;
    internal int StolenItem = -1, StolenBy, StolenUntil;
    internal bool TimekeeperSlowed;
    /// <summary>Cooldown timers of item weapons, per item definition.</summary>
    public required float[] ItemTimers;
    public required float[] ItemPulseTimers;
    public float AttackTimer;
    public int DashSpeedTicks, PhysicalBlockTick, ShieldRechargeTick;
    public float Shield, RefundCarry;
    public int AcceleratorTicks;
    public readonly List<int> EffectItems = [];
    public int DashChargesUsed, DashTicksTotal, BoughtChests, IllusionUntilTick, MaskIllusionUntilTick;
    public float BonusShield, AuraBlockChance, PhysicalAuraBonus;
    public Vector2 IllusionPosition, MaskIllusionPosition;
    public int BoarWalkTicks;
    public int GhostUntilTick, HeroAttackTick = -1;
    internal int NextHeroPassiveTick, Souls, IllusionHurtTick;
    internal float IllusionHealth, IllusionMaxHealth, PendingBlastDamage, HeroDistance, PuppeteerDamageBonus;
    internal readonly float[] CubeBonuses = new float[RunWorld.CubeStatCount];
    public int ConversionItem = -1;
    public float AcceleratorSpeedBonus;
    /// <summary>Fractional gold carried between pickups so small multipliers (+10% gold) are never rounded away.</summary>
    public float GoldCarry;
    public int InvulnTicks, DashTicks, DashCooldownTicks;
    public Vector2 DashDir;
    public int Kills;
    public int HurtTick = -100;
    public bool PrevDash;
    public int LastAppliedInput;
}
