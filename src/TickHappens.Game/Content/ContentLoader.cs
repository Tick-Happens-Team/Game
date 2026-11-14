using System.Text.Json;

namespace TickHappens.Game.Content;

public sealed class ContentException(IReadOnlyList<string> errors)
    : Exception("Content validation failed:\n  " + string.Join("\n  ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Loads and validates a content mod directory (characters, items, enemies, waves, rules).</summary>
public static class ContentLoader
{
    public static readonly string[] WeaponKinds = ["bolt", "pulse", "chain", "blast"];

    /// <summary>Finds content/core next to the executable (release) or in the Game repository (development).</summary>
    public static string FindCore()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "content", "core");
            if (File.Exists(Path.Combine(candidate, "mod.toml"))) return candidate;
        }
        throw new DirectoryNotFoundException("content/core not found next to the game or in the repository");
    }

    public static ContentSet Load(string dir)
    {
        var errors = new List<string>();
        T Read<T>(string file) where T : new()
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) { errors.Add($"{file}: missing"); return new T(); }
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ContentSet.Json) ?? new T(); }
            catch (JsonException e) { errors.Add($"{file}: {e.Message}"); return new T(); }
        }
        var set = new ContentSet
        {
            Characters = Read<List<CharacterDef>>("characters.json"),
            Items = Read<List<ItemDef>>("items.json"),
            Enemies = Read<List<EnemyDef>>("enemies.json"),
            Waves = Read<WaveTable>("waves.json"),
            Rules = Read<RunRules>("rules.json"),
        };
        Validate(set, errors);
        if (errors.Count > 0) throw new ContentException(errors);
        return set;
    }

    public static bool TryParseStat(string name, out Stat stat) =>
        Enum.TryParse(name, ignoreCase: true, out stat) && Enum.IsDefined(stat);

    public static bool TryParseDamage(string name, out DamageKind kind) =>
        Enum.TryParse(name, ignoreCase: true, out kind) && Enum.IsDefined(kind);

    private static string StatNames() => string.Join(", ", Enum.GetNames<Stat>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));

    private static void Validate(ContentSet c, List<string> e)
    {
        void Unique<T>(string file, IEnumerable<T> list, Func<T, string> id)
        {
            foreach (var g in list.GroupBy(id))
            {
                if (string.IsNullOrWhiteSpace(g.Key)) e.Add($"{file}: entry without id");
                else if (g.Count() > 1) e.Add($"{file}: duplicate id '{g.Key}'");
            }
        }
        void CheckStat(string where, string stat)
        {
            if (!TryParseStat(stat, out _)) e.Add($"{where}: unknown stat '{stat}' (known: {StatNames()})");
        }
        void CheckDamage(string where, string type)
        {
            if (!TryParseDamage(type, out _)) e.Add($"{where}: damageType '{type}' must be physical/magical/pure");
        }
        void CheckResist(string where, string field, float v)
        {
            if (v < 0 || v >= 1) e.Add(FormattableString.Invariant($"{where}: {field} {v} must be a fraction in [0, 1)"));
        }

        Unique("characters.json", c.Characters, x => x.Id);
        Unique("items.json", c.Items, x => x.Id);
        Unique("enemies.json", c.Enemies, x => x.Id);
        if (c.Characters.Count == 0) e.Add("characters.json: no characters");

        foreach (var ch in c.Characters)
        {
            string w = $"characters.json '{ch.Id}'";
            var s = ch.Stats;
            if (s.MaxHp <= 0 || s.MoveSpeed <= 0 || s.AttackSpeed <= 0 || s.Damage < 0) e.Add($"{w}: maxHp, moveSpeed, attackSpeed > 0 and damage >= 0 required");
            CheckResist(w, "armor", s.Armor);
            CheckResist(w, "magicResist", s.MagicResist);
            if (string.IsNullOrEmpty(ch.Sprite)) e.Add($"{w}: sprite required");
            if (ch.Attack != null) CheckDamage(w, ch.Attack.DamageType);
            foreach (var p in ch.Passives)
            {
                CheckStat(w, p.Stat);
                if (p.Per is not (null or "runMinute")) e.Add($"{w}: passive 'per' must be omitted or 'runMinute', got '{p.Per}'");
            }
        }
        foreach (var it in c.Items)
        {
            string w = $"items.json '{it.Id}'";
            if (!ContentSet.Rarities.Contains(it.Rarity)) e.Add($"{w}: rarity '{it.Rarity}' must be one of {string.Join("/", ContentSet.Rarities)}");
            if (it.Mods.Count == 0 && it.Weapon == null) e.Add($"{w}: needs stat mods or a weapon");
            foreach (var m in it.Mods) CheckStat(w, m.Stat);
            if (it.Weapon is { } wp)
            {
                if (!WeaponKinds.Contains(wp.Kind)) e.Add($"{w}: weapon kind '{wp.Kind}' must be one of {string.Join("/", WeaponKinds)}");
                CheckDamage(w, wp.DamageType);
                if (wp.Damage <= 0 || wp.Cooldown <= 0.05f || wp.Count < 1) e.Add($"{w}: weapon needs damage > 0, cooldown > 0.05, count >= 1");
                if (wp.Kind == "bolt" && wp.Speed <= 0) e.Add($"{w}: bolt needs speed > 0");
                if (wp.Kind is "pulse" or "blast" or "chain" && wp.Area <= 0) e.Add($"{w}: {wp.Kind} needs area > 0");
            }
        }
        foreach (var en in c.Enemies)
        {
            string w = $"enemies.json '{en.Id}'";
            if (en.Hp <= 0 || en.Speed < 0 || en.Radius <= 0) e.Add($"{w}: hp > 0, speed >= 0, radius > 0 required");
            if (string.IsNullOrEmpty(en.Sprite)) e.Add($"{w}: sprite required");
            if (en.Tint is { } t && (t.Length != 6 || !t.All(Uri.IsHexDigit))) e.Add($"{w}: tint must be RRGGBB");
            CheckDamage(w, en.DamageType);
            CheckResist(w, "armor", en.Armor);
            CheckResist(w, "magicResist", en.MagicResist);
        }
        float prevTo = 0;
        foreach (var wv in c.Waves.Waves.OrderBy(x => x.From))
        {
            string w = FormattableString.Invariant($"waves.json wave {wv.From}-{wv.To}");
            if (wv.From != prevTo) e.Add(FormattableString.Invariant($"{w}: waves must cover time without gaps (expected start {prevTo})"));
            if (wv.To <= wv.From || wv.PerSecond <= 0 || wv.Mix.Count == 0) e.Add($"{w}: needs to > from, perSecond > 0 and a mix");
            foreach (var id in wv.Mix.Keys) if (c.EnemyIndex(id) < 0) e.Add($"{w}: unknown enemy '{id}'");
            prevTo = wv.To;
        }
        if (c.Waves.Waves.Count == 0) e.Add("waves.json: no waves");
        foreach (var el in c.Waves.Elites) if (c.EnemyIndex(el.Enemy) < 0) e.Add($"waves.json elite at {el.At}: unknown enemy '{el.Enemy}'");
        var r = c.Rules;
        if (r.ArenaSize < 512) e.Add("rules.json: arenaSize must be >= 512");
        if (r.Chests.PriceGrowth != 1.5m) e.Add("rules.json: chests.priceGrowth is fixed at 1.5 by the game design");
        foreach (var k in r.Chests.RarityWeights.Keys)
            if (!ContentSet.Rarities.Contains(k)) e.Add($"rules.json: chests.rarityWeights has unknown rarity '{k}'");
        if (c.Items.Count > 0 && !c.Items.Any(i => r.Chests.RarityWeights.GetValueOrDefault(i.Rarity) > 0))
            e.Add("rules.json: chests.rarityWeights gives zero weight to every item rarity");
        CheckDamage("rules.json basicAttack", r.BasicAttack.DamageType);
        if (r.Units.MoveSpeedScale <= 0 || r.Units.AttacksPerUnit <= 0 || r.Units.ResistCap <= 0 || r.Units.ResistCap >= 1)
            e.Add("rules.json: units need moveSpeedScale > 0, attacksPerUnit > 0 and 0 < resistCap < 1");
    }
}
