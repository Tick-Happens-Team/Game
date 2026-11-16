using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickHappens.Game.Content;

/// <summary>Stats that characters, passives and items modify. JSON uses the camelCase names.</summary>
public enum Stat
{
    /// <summary>Max HP before <see cref="HpMultiplier"/>; grows per level from the character table.</summary>
    MaxHp,
    /// <summary>Final max HP multiplier (Небесный маг: ×0.35). Default 1.</summary>
    HpMultiplier,
    /// <summary>Physical damage reduction as a fraction (0.05 = 5%), capped at 90%.</summary>
    Armor,
    /// <summary>Magical damage reduction as a fraction, capped at 90%.</summary>
    MagicResist,
    /// <summary>Movement speed in table units (С/П); px/s = value × rules.units.moveSpeedScale.</summary>
    MoveSpeed,
    /// <summary>Attack speed in table units (С/А); attacks/s = value × rules.units.attacksPerUnit.</summary>
    AttackSpeed,
    /// <summary>Basic attack damage; grows per level from the character table.</summary>
    Damage,
    Regen, Magnet,
    /// <summary>Multiplier on all outgoing damage. Default 1.</summary>
    Might,
    /// <summary>Multipliers by damage type. Default 1.</summary>
    PhysicalDamage, MagicalDamage,
    /// <summary>Multiplier on damage dealt by items (not the basic attack). Default 1.</summary>
    ItemDamage,
    /// <summary>Area multiplier for item radii. Default 1.</summary>
    Area,
    /// <summary>Cooldown multiplier for items and abilities, the dash included (lower is faster). Default 1.</summary>
    Cooldown,
    /// <summary>Extra projectiles / targets added to attacks.</summary>
    Amount,
    /// <summary>Multiplier on collected gold. Default 1.</summary>
    GoldGain,
    /// <summary>Dash cooldown multiplier. Default 1.</summary>
    DashCooldown,
    PhysicalCrit, MagicalCrit, CritDamage, MagicalLifesteal,
    StationaryRegen, RechargingShield, GoldRefund, DashSpeedBonus,
    EnemyLifestealCap, PhysicalBlock, DamageReroll, FinisherChance, DamageReflection,
    EliteDamage, HighestHealthDamage, DashLength, ExtraDashCharges, Lifesteal,
    /// <summary>Multiplier on damage the hero takes after defences (ally auras lower it). Default 1, floor 0.1.</summary>
    DamageTaken,
}

public enum DamageKind { Physical, Magical, Pure }

public sealed class StatMod
{
    [JsonPropertyName("stat")] public string Stat { get; set; } = "";
    [JsonPropertyName("add")] public float Add { get; set; }
    [JsonPropertyName("pct")] public float Pct { get; set; }
}

public sealed class Growth
{
    [JsonPropertyName("add")] public float Add { get; set; }
    [JsonPropertyName("pct")] public float Pct { get; set; }
}

/// <summary>
/// A character passive as a level-scaled stat formula:
/// value(level) = base + perLevel × (level − 1), optionally multiplied by the run clock in minutes ("per": "runMinute").
/// </summary>
public sealed class PassiveDef
{
    [JsonPropertyName("stat")] public string Stat { get; set; } = "";
    [JsonPropertyName("add")] public float Add { get; set; }
    [JsonPropertyName("pct")] public float Pct { get; set; }
    [JsonPropertyName("perLevel")] public Growth? PerLevel { get; set; }
    /// <summary>null or "runMinute" (scaled by the game run clock, statues included).</summary>
    [JsonPropertyName("per")] public string? Per { get; set; }
}

public sealed class CharacterStats
{
    [JsonPropertyName("maxHp")] public float MaxHp { get; set; } = 100;
    [JsonPropertyName("maxHpPerLevel")] public float MaxHpPerLevel { get; set; }
    [JsonPropertyName("moveSpeed")] public float MoveSpeed { get; set; } = 25;
    [JsonPropertyName("attackSpeed")] public float AttackSpeed { get; set; } = 25;
    [JsonPropertyName("armor")] public float Armor { get; set; }
    [JsonPropertyName("magicResist")] public float MagicResist { get; set; }
    [JsonPropertyName("damage")] public float Damage { get; set; } = 10;
    [JsonPropertyName("damagePerLevel")] public float DamagePerLevel { get; set; }
    [JsonPropertyName("regen")] public float Regen { get; set; }
    [JsonPropertyName("magnet")] public float Magnet { get; set; } = 34;
}

/// <summary>Basic attack definition shared by the default projectiles and hero overrides.</summary>
public sealed class AttackDef
{
    [JsonPropertyName("meleeAngle")] public float MeleeAngle { get; set; }
    [JsonPropertyName("rangePerLevel")] public float RangePerLevel { get; set; }
    [JsonPropertyName("damageType")] public string DamageType { get; set; } = "physical";
    [JsonPropertyName("speed")] public float Speed { get; set; } = 230;
    [JsonPropertyName("range")] public float Range { get; set; } = 220;
    [JsonPropertyName("pierce")] public int Pierce { get; set; } = 1;
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
}

/// <summary>
/// Character ally aura (Некотян): every living ally within the radius, the owner and allied summons included,
/// takes less damage and regenerates HP. value(level) = base + perLevel × (level − 1); the owner gets
/// <see cref="SelfMultiplier"/> × the damage reduction and regen. The radius scales with the owner's Area.
/// </summary>
public sealed class CharacterAura
{
    [JsonPropertyName("radius")] public float Radius { get; set; }
    [JsonPropertyName("radiusPerLevel")] public float RadiusPerLevel { get; set; }
    /// <summary>Fraction of incoming damage removed (0.08 = 8%).</summary>
    [JsonPropertyName("damageReduction")] public float DamageReduction { get; set; }
    [JsonPropertyName("damageReductionPerLevel")] public float DamageReductionPerLevel { get; set; }
    /// <summary>HP restored per second.</summary>
    [JsonPropertyName("regen")] public float Regen { get; set; }
    [JsonPropertyName("regenPerLevel")] public float RegenPerLevel { get; set; }
    [JsonPropertyName("selfMultiplier")] public float SelfMultiplier { get; set; } = 1;
}

public sealed class CharacterDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    /// <summary>Passive as written by the designer (shown on the character screen).</summary>
    [JsonPropertyName("passive")] public string Passive { get; set; } = "";
    /// <summary>Passive growth per level as written by the designer.</summary>
    [JsonPropertyName("growth")] public string Growth { get; set; } = "";
    [JsonPropertyName("stats")] public CharacterStats Stats { get; set; } = new();
    /// <summary>Optional basic attack override; defaults come from rules.basicAttack.</summary>
    [JsonPropertyName("attack")] public AttackDef? Attack { get; set; }
    [JsonPropertyName("passives")] public List<PassiveDef> Passives { get; set; } = [];
    [JsonPropertyName("aura")] public CharacterAura? Aura { get; set; }
}

/// <summary>Active behaviour of an item that attacks on its own (see ContentLoader.WeaponKinds).</summary>
public sealed class ItemWeapon
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("damageType")] public string DamageType { get; set; } = "physical";
    [JsonPropertyName("damage")] public float Damage { get; set; }
    [JsonPropertyName("cooldown")] public float Cooldown { get; set; } = 1;
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
    [JsonPropertyName("speed")] public float Speed { get; set; } = 230;
    [JsonPropertyName("pierce")] public int Pierce { get; set; } = 1;
    [JsonPropertyName("range")] public float Range { get; set; } = 200;
    [JsonPropertyName("area")] public float Area { get; set; }
    [JsonPropertyName("angle")] public float Angle { get; set; }
    [JsonPropertyName("bossDamage")] public float BossDamage { get; set; }
    [JsonPropertyName("duration")] public float Duration { get; set; }
    [JsonPropertyName("speedBonus")] public float SpeedBonus { get; set; }
    [JsonPropertyName("minSlow")] public float? MinSlow { get; set; }
    [JsonPropertyName("minDamage")] public float MinDamage { get; set; }
    [JsonPropertyName("slow")] public float Slow { get; set; }
    [JsonPropertyName("chance")] public float Chance { get; set; }
}

public sealed class CompanionDef
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    [JsonPropertyName("damageType")] public string DamageType { get; set; } = "physical";
    [JsonPropertyName("interval")] public float Interval { get; set; }
    [JsonPropertyName("attackInterval")] public float AttackInterval { get; set; }
    [JsonPropertyName("duration")] public float Duration { get; set; }
    [JsonPropertyName("health")] public float Health { get; set; }
    [JsonPropertyName("damage")] public float Damage { get; set; }
    [JsonPropertyName("speed")] public float Speed { get; set; }
    [JsonPropertyName("range")] public float Range { get; set; }
    [JsonPropertyName("area")] public float Area { get; set; }
    [JsonPropertyName("slow")] public float Slow { get; set; }
    [JsonPropertyName("stun")] public float Stun { get; set; }
    [JsonPropertyName("chance")] public float Chance { get; set; }
}

public sealed class ItemEffect
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("value")] public float Value { get; set; }
    [JsonPropertyName("secondary")] public float Secondary { get; set; }
    [JsonPropertyName("chance")] public float Chance { get; set; }
    [JsonPropertyName("duration")] public float Duration { get; set; }
    [JsonPropertyName("radius")] public float Radius { get; set; }
    [JsonPropertyName("threshold")] public float Threshold { get; set; }
}

public sealed class ItemDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>common | rare | epic | mythic | legendary | cursed</summary>
    [JsonPropertyName("rarity")] public string Rarity { get; set; } = "common";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Applied once per copy owned (copies stack).</summary>
    [JsonPropertyName("mods")] public List<StatMod> Mods { get; set; } = [];
    /// <summary>Optional attack behaviour; extra copies add +1 count.</summary>
    [JsonPropertyName("weapon")] public ItemWeapon? Weapon { get; set; }
    [JsonPropertyName("xpOnPickup")] public float XpOnPickup { get; set; }
    [JsonPropertyName("companion")] public CompanionDef? Companion { get; set; }
    [JsonPropertyName("effect")] public ItemEffect? Effect { get; set; }
    [JsonPropertyName("bossDrop")] public string? BossDrop { get; set; }
}

public sealed class EnemyDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    [JsonPropertyName("anim")] public string Anim { get; set; } = "";
    /// <summary>Optional RRGGBB tint multiplied into the sprite (elite variants).</summary>
    [JsonPropertyName("tint")] public string? Tint { get; set; }
    [JsonPropertyName("hp")] public float Hp { get; set; }
    /// <summary>Movement speed in table units (С/П); px/s = value × rules.units.moveSpeedScale.</summary>
    [JsonPropertyName("speed")] public float Speed { get; set; }
    [JsonPropertyName("damage")] public float Damage { get; set; }
    [JsonPropertyName("damageType")] public string DamageType { get; set; } = "physical";
    /// <summary>Share of damage dealt to a player that heals this enemy (Вампиризм).</summary>
    [JsonPropertyName("lifesteal")] public float Lifesteal { get; set; }
    [JsonPropertyName("armor")] public float Armor { get; set; }
    [JsonPropertyName("magicResist")] public float MagicResist { get; set; }
    [JsonPropertyName("radius")] public float Radius { get; set; } = 6;
    [JsonPropertyName("xp")] public int Xp { get; set; } = 1;
    [JsonPropertyName("gold")] public int Gold { get; set; }
    [JsonPropertyName("behavior")] public string Behavior { get; set; } = "";
    [JsonPropertyName("goldChance")] public float GoldChance { get; set; }
    /// <summary>Bosses are excluded from execute effects.</summary>
    [JsonPropertyName("boss")] public bool Boss { get; set; }
    [JsonPropertyName("elite")] public bool Elite { get; set; }
    [JsonPropertyName("attackSpeed")] public float AttackSpeed { get; set; }
    [JsonPropertyName("shotRange")] public float ShotRange { get; set; }
    [JsonPropertyName("shotSpeed")] public float ShotSpeed { get; set; }
    [JsonPropertyName("detonateRange")] public float DetonateRange { get; set; }
    [JsonPropertyName("blastRadius")] public float BlastRadius { get; set; }
    [JsonPropertyName("cloudRadius")] public float CloudRadius { get; set; }
    [JsonPropertyName("cloudSeconds")] public float CloudSeconds { get; set; }
    [JsonPropertyName("siphonDps")] public float SiphonDps { get; set; } = 5;
    [JsonPropertyName("cloudDps")] public float CloudDps { get; set; }
}

public sealed class WaveDef
{
    [JsonPropertyName("from")] public float From { get; set; }
    [JsonPropertyName("to")] public float To { get; set; }
    [JsonPropertyName("perSecond")] public float PerSecond { get; set; }
    [JsonPropertyName("mix")] public Dictionary<string, float> Mix { get; set; } = [];
}

public sealed class EliteDef
{
    [JsonPropertyName("at")] public float At { get; set; }
    [JsonPropertyName("enemy")] public string Enemy { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
}

public sealed class WaveTable
{
    [JsonPropertyName("hpPerMinute")] public float HpPerMinute { get; set; }
    [JsonPropertyName("hpPerMinuteSquared")] public float HpPerMinuteSquared { get; set; }
    [JsonPropertyName("damagePerMinute")] public float DamagePerMinute { get; set; }
    [JsonPropertyName("maxAlive")] public int MaxAlive { get; set; } = 800;
    [JsonPropertyName("waves")] public List<WaveDef> Waves { get; set; } = [];
    [JsonPropertyName("bossIntervalMinutes")] public int BossIntervalMinutes { get; set; }
    [JsonPropertyName("xpPerMinute")] public float XpPerMinute { get; set; }
    [JsonPropertyName("goldPerMinute")] public int GoldPerMinute { get; set; }
    [JsonPropertyName("elites")] public List<EliteDef> Elites { get; set; } = [];
}

public sealed class DashRules
{
    [JsonPropertyName("distance")] public float Distance { get; set; } = 46;
    [JsonPropertyName("seconds")] public float Seconds { get; set; } = 0.16f;
    [JsonPropertyName("cooldown")] public float Cooldown { get; set; } = 1.6f;
    [JsonPropertyName("invulnerable")] public bool Invulnerable { get; set; } = true;
}

public sealed class StatueRules
{
    [JsonPropertyName("count")] public int Count { get; set; } = 5;
    [JsonPropertyName("minutes")] public float Minutes { get; set; } = 2;
    [JsonPropertyName("radius")] public float Radius { get; set; } = 14;
    [JsonPropertyName("holdSeconds")] public float HoldSeconds { get; set; } = 1;
}

public sealed class ChestRules
{
    [JsonPropertyName("count")] public int Count { get; set; } = 6;
    [JsonPropertyName("radius")] public float Radius { get; set; } = 18;
    [JsonPropertyName("freeCount")] public int FreeCount { get; set; }
    [JsonPropertyName("rareFreeChest")] public bool RareFreeChest { get; set; }
    [JsonPropertyName("startPrice")] public int StartPrice { get; set; } = 10;
    [JsonPropertyName("priceGrowth")] public decimal PriceGrowth { get; set; } = 1.5m;
    /// <summary>Loot weight per rarity; rarities without items are skipped when rolling.</summary>
    [JsonPropertyName("rarityWeights")] public Dictionary<string, float> RarityWeights { get; set; } = [];
}

/// <summary>Conversion from designer table units to engine units.</summary>
public sealed class UnitRules
{
    /// <summary>px/s per С/П point (25 → 75 px/s).</summary>
    [JsonPropertyName("moveSpeedScale")] public float MoveSpeedScale { get; set; } = 3;
    /// <summary>Attacks per second per С/А point (25 → 1 attack/s).</summary>
    [JsonPropertyName("attacksPerUnit")] public float AttacksPerUnit { get; set; } = 0.04f;
    /// <summary>Upper bound for armor and magic resist.</summary>
    [JsonPropertyName("resistCap")] public float ResistCap { get; set; } = 0.9f;
}

public sealed class RunRules
{
    [JsonPropertyName("arenaSize")] public int ArenaSize { get; set; } = 2048;
    [JsonPropertyName("xpBase")] public float XpBase { get; set; } = 5;
    [JsonPropertyName("xpGrowth")] public float XpGrowth { get; set; } = 1.18f;
    [JsonPropertyName("invulnerableSeconds")] public float InvulnerableSeconds { get; set; } = 0.1f;
    [JsonPropertyName("units")] public UnitRules Units { get; set; } = new();
    [JsonPropertyName("basicAttack")] public AttackDef BasicAttack { get; set; } = new();
    [JsonPropertyName("dash")] public DashRules Dash { get; set; } = new();
    [JsonPropertyName("statue")] public StatueRules Statue { get; set; } = new();
    [JsonPropertyName("chests")] public ChestRules Chests { get; set; } = new();
}

/// <summary>All content of a run, loaded from a mod directory (built-in "core" for now).</summary>
public sealed class ContentSet
{
    public static readonly string[] Rarities = ["common", "rare", "epic", "mythic", "legendary"];
    public const string CursedRarity = "cursed";

    public required List<CharacterDef> Characters { get; init; }
    public required List<ItemDef> Items { get; init; }
    public required List<EnemyDef> Enemies { get; init; }
    public required WaveTable Waves { get; init; }
    public required RunRules Rules { get; init; }

    public int CharacterIndex(string id) => Characters.FindIndex(c => c.Id == id);
    public int ItemIndex(string id) => Items.FindIndex(i => i.Id == id);
    public int EnemyIndex(string id) => Enemies.FindIndex(e => e.Id == id);

    internal static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
