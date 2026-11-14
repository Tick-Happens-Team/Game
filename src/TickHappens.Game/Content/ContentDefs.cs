using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickHappens.Game.Content;

/// <summary>Stats that items, passives and characters modify. JSON uses the camelCase names.</summary>
public enum Stat
{
    MaxHp, Armor, MoveSpeed, Regen, Magnet,
    /// <summary>Damage multiplier (1 = base).</summary>
    Might,
    /// <summary>Area multiplier for weapon radii.</summary>
    Area,
    /// <summary>Cooldown multiplier (lower is faster).</summary>
    Cooldown,
    /// <summary>Extra projectiles / targets added to every weapon.</summary>
    Amount,
}

public sealed class StatMod
{
    [JsonPropertyName("stat")] public string Stat { get; set; } = "";
    [JsonPropertyName("add")] public float Add { get; set; }
    [JsonPropertyName("pct")] public float Pct { get; set; }
}

public sealed class PassiveDef
{
    /// <summary>Event that triggers the passive: "levelUp".</summary>
    [JsonPropertyName("on")] public string On { get; set; } = "";
    [JsonPropertyName("stat")] public string Stat { get; set; } = "";
    [JsonPropertyName("add")] public float Add { get; set; }
    [JsonPropertyName("pct")] public float Pct { get; set; }
}

public sealed class CharacterDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("stats")] public Dictionary<string, float> Stats { get; set; } = [];
    [JsonPropertyName("weapon")] public string Weapon { get; set; } = "";
    [JsonPropertyName("passives")] public List<PassiveDef> Passives { get; set; } = [];
}

public sealed class WeaponLevel
{
    [JsonPropertyName("damage")] public float Damage { get; set; }
    [JsonPropertyName("cooldown")] public float Cooldown { get; set; } = 1;
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
    [JsonPropertyName("speed")] public float Speed { get; set; }
    [JsonPropertyName("pierce")] public int Pierce { get; set; } = 1;
    [JsonPropertyName("range")] public float Range { get; set; } = 200;
    [JsonPropertyName("area")] public float Area { get; set; }
}

public sealed class WeaponDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>bolt | pulse | chain | blast</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("damageType")] public string DamageType { get; set; } = "physical";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("levels")] public List<WeaponLevel> Levels { get; set; } = [];
}

public sealed class ItemDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("maxLevel")] public int MaxLevel { get; set; } = 5;
    /// <summary>Applied once per item level.</summary>
    [JsonPropertyName("mods")] public List<StatMod> Mods { get; set; } = [];
}

public sealed class EnemyDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    [JsonPropertyName("anim")] public string Anim { get; set; } = "";
    /// <summary>Optional RRGGBB tint multiplied into the sprite (elite variants).</summary>
    [JsonPropertyName("tint")] public string? Tint { get; set; }
    [JsonPropertyName("hp")] public float Hp { get; set; }
    [JsonPropertyName("speed")] public float Speed { get; set; }
    [JsonPropertyName("damage")] public float Damage { get; set; }
    [JsonPropertyName("radius")] public float Radius { get; set; } = 6;
    [JsonPropertyName("xp")] public int Xp { get; set; } = 1;
    [JsonPropertyName("gold")] public int Gold { get; set; }
    [JsonPropertyName("goldChance")] public float GoldChance { get; set; }
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
    [JsonPropertyName("damagePerMinute")] public float DamagePerMinute { get; set; }
    [JsonPropertyName("maxAlive")] public int MaxAlive { get; set; } = 800;
    [JsonPropertyName("waves")] public List<WaveDef> Waves { get; set; } = [];
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
    [JsonPropertyName("startPrice")] public int StartPrice { get; set; } = 10;
    [JsonPropertyName("priceGrowth")] public decimal PriceGrowth { get; set; } = 1.5m;
}

public sealed class RunRules
{
    [JsonPropertyName("arenaSize")] public int ArenaSize { get; set; } = 2048;
    [JsonPropertyName("maxWeapons")] public int MaxWeapons { get; set; } = 4;
    [JsonPropertyName("maxItems")] public int MaxItems { get; set; } = 4;
    [JsonPropertyName("xpBase")] public float XpBase { get; set; } = 5;
    [JsonPropertyName("xpGrowth")] public float XpGrowth { get; set; } = 1.18f;
    [JsonPropertyName("invulnerableSeconds")] public float InvulnerableSeconds { get; set; } = 0.5f;
    [JsonPropertyName("dash")] public DashRules Dash { get; set; } = new();
    [JsonPropertyName("statue")] public StatueRules Statue { get; set; } = new();
    [JsonPropertyName("chests")] public ChestRules Chests { get; set; } = new();
}

/// <summary>All content of a run, loaded from a mod directory (built-in "core" for now).</summary>
public sealed class ContentSet
{
    public required List<CharacterDef> Characters { get; init; }
    public required List<WeaponDef> Weapons { get; init; }
    public required List<ItemDef> Items { get; init; }
    public required List<EnemyDef> Enemies { get; init; }
    public required WaveTable Waves { get; init; }
    public required RunRules Rules { get; init; }

    public int WeaponIndex(string id) => Weapons.FindIndex(w => w.Id == id);
    public int ItemIndex(string id) => Items.FindIndex(i => i.Id == id);
    public int EnemyIndex(string id) => Enemies.FindIndex(e => e.Id == id);

    internal static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
