using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

/// <summary>Per-tick player input. Movement and dash are the only manual gameplay actions.</summary>
public readonly record struct RunInput(int X, int Y, bool Dash = false);

public enum RunPhase { Playing, Dead }

public enum EventKind : byte
{
    Hit, Kill, Bolt, Pulse, Chain, Blast, LevelUp, Statue, Chest, PlayerHurt, Dash, Pickup, Elite,
}

/// <summary>Gameplay event for cosmetics (VFX, sound, damage numbers). Never drives simulation.</summary>
public struct GameEvent
{
    public EventKind Kind;
    public float X, Y, X2, Y2, Value;
    /// <summary>Chain: start index into <see cref="RunWorld.ChainPoints"/>; Hit: damage kind; Chest: item index; otherwise kind-specific.</summary>
    public int A, B;
    /// <summary>Deterministic per-event seed for cosmetic randomness.</summary>
    public uint Seed;
}

/// <summary>Small deterministic generator (xorshift32). Same seed, same sequence, on every machine.</summary>
public struct SimRng(uint seed)
{
    private uint _s = seed == 0 ? 0x9E3779B9u : seed;

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
public readonly record struct DamageSourceRef(int PlayerId, int ItemDef = -1)
{
    public bool FromItem => ItemDef >= 0;
}

public sealed class PlayerState
{
    public int Id;
    public Vector2 Pos, Prev, Facing = Vector2.UnitY;
    public bool FacingLeft, Walking;
    public float Hp;
    public int Level = 1;
    public float Xp, XpNext;
    public required StatBlock Stats;
    public required ChestWallet Wallet;
    public required CharacterDef Character;
    public required AttackDef Attack;
    /// <summary>Copies owned per item definition (items come only from chests and stack).</summary>
    public required int[] ItemStacks;
    /// <summary>Cooldown timers of item weapons, per item definition.</summary>
    public required float[] ItemTimers;
    public float AttackTimer;
    /// <summary>Fractional gold carried between pickups so small multipliers (+10% gold) are never rounded away.</summary>
    public float GoldCarry;
    public int InvulnTicks, DashTicks, DashCooldownTicks;
    public Vector2 DashDir;
    public int Kills;
    public int HurtTick = -100;
    public bool PrevDash;
}
