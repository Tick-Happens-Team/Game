using System.Text.Json;

namespace TickHappens.Game.Content;

public sealed class ContentException(IReadOnlyList<string> errors)
    : Exception("Content validation failed:\n  " + string.Join("\n  ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Loads and validates a content mod directory (characters, weapons, items, enemies, waves, rules).</summary>
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
            Weapons = Read<List<WeaponDef>>("weapons.json"),
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
            if (!TryParseStat(stat, out _)) e.Add($"{where}: unknown stat '{stat}' (known: {string.Join(", ", Enum.GetNames<Stat>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]))})");
        }

        Unique("characters.json", c.Characters, x => x.Id);
        Unique("weapons.json", c.Weapons, x => x.Id);
        Unique("items.json", c.Items, x => x.Id);
        Unique("enemies.json", c.Enemies, x => x.Id);
        if (c.Characters.Count == 0) e.Add("characters.json: no characters");

        foreach (var ch in c.Characters)
        {
            string w = $"characters.json '{ch.Id}'";
            if (c.WeaponIndex(ch.Weapon) < 0) e.Add($"{w}: unknown starting weapon '{ch.Weapon}'");
            foreach (var s in ch.Stats.Keys) CheckStat(w, s);
            foreach (var p in ch.Passives)
            {
                if (p.On != "levelUp") e.Add($"{w}: passive trigger '{p.On}' is not supported (levelUp)");
                CheckStat(w, p.Stat);
            }
        }
        foreach (var wd in c.Weapons)
        {
            string w = $"weapons.json '{wd.Id}'";
            if (!WeaponKinds.Contains(wd.Kind)) e.Add($"{w}: kind '{wd.Kind}' must be one of {string.Join("/", WeaponKinds)}");
            if (!Enum.TryParse<DamageType>(wd.DamageType, true, out _)) e.Add($"{w}: damageType '{wd.DamageType}' must be physical/magical/pure");
            if (wd.Levels.Count == 0) e.Add($"{w}: needs at least one level");
            for (int i = 0; i < wd.Levels.Count; i++)
            {
                var l = wd.Levels[i];
                if (l.Damage <= 0 || l.Cooldown <= 0.05f || l.Count < 1) e.Add($"{w} level {i + 1}: damage > 0, cooldown > 0.05 and count >= 1 required");
                if (wd.Kind == "bolt" && l.Speed <= 0) e.Add($"{w} level {i + 1}: bolt needs speed > 0");
                if (wd.Kind is "pulse" or "blast" or "chain" && l.Area <= 0) e.Add($"{w} level {i + 1}: {wd.Kind} needs area > 0");
            }
        }
        foreach (var it in c.Items)
        {
            string w = $"items.json '{it.Id}'";
            if (it.MaxLevel < 1) e.Add($"{w}: maxLevel must be >= 1");
            if (it.Mods.Count == 0) e.Add($"{w}: needs at least one stat mod");
            foreach (var m in it.Mods) CheckStat(w, m.Stat);
        }
        foreach (var en in c.Enemies)
        {
            string w = $"enemies.json '{en.Id}'";
            if (en.Hp <= 0 || en.Speed < 0 || en.Radius <= 0) e.Add($"{w}: hp > 0, speed >= 0, radius > 0 required");
            if (string.IsNullOrEmpty(en.Sprite)) e.Add($"{w}: sprite required");
            if (en.Tint is { } t && (t.Length != 6 || !t.All(Uri.IsHexDigit))) e.Add($"{w}: tint must be RRGGBB");
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
        if (r.MaxWeapons < 1 || r.MaxItems < 0) e.Add("rules.json: maxWeapons >= 1 and maxItems >= 0 required");
    }
}
