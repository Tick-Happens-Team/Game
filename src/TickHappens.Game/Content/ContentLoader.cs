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
    public static readonly string[] WeaponKinds = ["bolt", "pulse", "chain", "blast", "melee", "execute", "snipe", "accelerator", "wood", "aura", "sustainedCone", "boomerang", "bounce", "dropped", "cane", "rocket", "attackChain"];

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

    public static ContentSet Load(string dir, string? modsRoot = null, string? profilePath = null)
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
        if (modsRoot is not null && profilePath is not null && File.Exists(profilePath))
            LoadItemMods(set, modsRoot, profilePath, errors);
        Validate(set, errors);
        if (errors.Count > 0) throw new ContentException(errors);
        // OrderBy is stable so simultaneous elite waves keep their authored spawn order.
        var elites = set.Waves.Elites.OrderBy(e => e.At).ToArray();
        set.Waves.Elites.Clear();
        set.Waves.Elites.AddRange(elites);
        return set;
    }

    private static void LoadItemMods(ContentSet set, string root, string profile, List<string> errors)
    {
        try
        {
            var enabled = JsonSerializer.Deserialize<string[]>(File.ReadAllText(profile)) ?? [];
            foreach (string id in enabled.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(id) || id.Length > 64 ||
                    !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_') || id == "core")
                {
                    errors.Add("Mod folder names must use lowercase ASCII letters, digits, hyphens or underscores.");
                    continue;
                }
                var items = JsonSerializer.Deserialize<List<ItemDef>>(
                    File.ReadAllText(Path.Combine(root, id, "items.json")), ContentSet.Json) ?? [];
                if (items.Any(item => !item.Id.StartsWith(id + ":", StringComparison.Ordinal) || item.Id.Length <= id.Length + 1))
                    errors.Add($"{id}: item IDs must use the mod's namespace.");
                else
                    set.Items.AddRange(items);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add($"Local mod profile: {error.Message}");
        }
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
        void CheckAttack(string where, AttackDef attack)
        {
            CheckDamage(where, attack.DamageType);
            if (!float.IsFinite(attack.MeleeAngle) || attack.MeleeAngle is < 0 or > 360 ||
                !float.IsFinite(attack.RangePerLevel) || attack.RangePerLevel < 0 ||
                attack.MeleeAngle > 0 && (!float.IsFinite(attack.Range) || attack.Range <= 0))
                e.Add($"{where}: invalid melee attack range, angle or growth");
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
            if (ch.Attack != null) CheckAttack(w, ch.Attack);
            foreach (var p in ch.Passives)
            {
                CheckStat(w, p.Stat);
                if (p.Per is not (null or "runMinute")) e.Add($"{w}: passive 'per' must be omitted or 'runMinute', got '{p.Per}'");
            }
            if (ch.Aura is { } aura)
            {
                if (aura.Radius <= 0 || aura.RadiusPerLevel < 0) e.Add($"{w}: aura radius must be > 0 and radiusPerLevel >= 0");
                if (aura.DamageReduction is < 0 or >= 1 || aura.DamageReductionPerLevel < 0 || aura.Regen < 0 || aura.RegenPerLevel < 0 || aura.SelfMultiplier <= 0)
                    e.Add($"{w}: aura damageReduction must be in [0, 1); growth, regen >= 0 and selfMultiplier > 0");
            }
        }
        foreach (var it in c.Items)
        {
            string w = $"items.json '{it.Id}'";
            if (it.Rarity != ContentSet.CursedRarity && !ContentSet.Rarities.Contains(it.Rarity))
                e.Add($"{w}: rarity '{it.Rarity}' must be one of {string.Join("/", ContentSet.Rarities.Append(ContentSet.CursedRarity))}");
            if (it.Rarity == ContentSet.CursedRarity && string.IsNullOrWhiteSpace(it.BossDrop))
                e.Add($"{w}: cursed item needs a bossDrop id");
            if (it.BossDrop is { } boss &&
                (it.Rarity != ContentSet.CursedRarity || c.Enemies.Find(enemy => enemy.Id == boss) is { Boss: false }))
                e.Add($"{w}: bossDrop '{boss}' must link a cursed item to a boss");
            if (it.XpOnPickup < 0) e.Add($"{w}: xpOnPickup must be nonnegative");
            if (it.Mods.Count == 0 && it.Weapon == null && it.XpOnPickup == 0 && it.Companion == null && it.Effect == null)
                e.Add($"{w}: needs stat mods, a weapon, pickup XP, a companion or an effect");
            foreach (var m in it.Mods) CheckStat(w, m.Stat);
            if (it.Effect is { } effect)
            {
                if (effect.Kind is not ("armorAura" or "attackSpeedAura" or "healthAura" or "physicalAura" or
                    "lifestealAura" or "summonRegenAura" or "healShare" or "lowestHealthShield" or
                    "weldingBlock" or "rearSlow" or "poison" or "icicle" or "revive" or "extraChestChoice" or
                    "illusion" or "resetCooldown" or "devour"))
                    e.Add($"{w}: unknown effect kind '{effect.Kind}'");
                if (effect.Value < 0 || effect.Secondary < 0 || effect.Chance is < 0 or > 1 ||
                    effect.Duration < 0 || effect.Radius < 0 || effect.Threshold < 0)
                    e.Add($"{w}: invalid effect parameters");
                if (effect.Kind == "weldingBlock" && effect.Value is <= 0 or >= 360)
                    e.Add($"{w}: weldingBlock needs a vision angle in (0, 360)");
                if (it.Weapon != null || it.Companion != null) e.Add($"{w}: effect needs its own item cooldown");
            }
            if (it.Companion is { } companion)
            {
                CheckDamage(w, companion.DamageType);
                if (companion.Kind is not ("gnome" or "dog" or "drone" or "boar" or "eagle" or "leech" or "demon"))
                    e.Add($"{w}: unknown companion kind '{companion.Kind}'");
                if (string.IsNullOrWhiteSpace(companion.Sprite) || companion.Speed <= 0 ||
                    companion.Interval < 0 || companion.AttackInterval < 0 || companion.Duration < 0 ||
                    companion.Health < 0 || companion.Damage < 0 || companion.Area < 0 ||
                    companion.Range < 0 || companion.Slow is < 0 or > 1 || companion.Stun < 0 ||
                    companion.Chance is < 0 or > 1)
                    e.Add($"{w}: invalid companion stats or missing sprite");
                if (it.Weapon != null) e.Add($"{w}: companion and weapon cannot share an item cooldown");
            }
            if (it.Weapon is { } wp)
            {
                if (!WeaponKinds.Contains(wp.Kind)) e.Add($"{w}: weapon kind '{wp.Kind}' must be one of {string.Join("/", WeaponKinds)}");
                CheckDamage(w, wp.DamageType);
                if ((wp.Kind != "execute" && wp.Damage <= 0) || wp.Cooldown <= 0.05f || wp.Count < 1)
                    e.Add($"{w}: weapon needs positive damage (except execute), cooldown > 0.05 and count >= 1");
                if (wp.Kind is "melee" or "sustainedCone" && (wp.Area <= 0 || wp.Angle is <= 0 or >= 360))
                    e.Add($"{w}: melee needs area > 0 and angle in (0, 360)");
                if (wp.Kind == "snipe" && (wp.Damage > 1 || wp.BossDamage is <= 0 or > 1))
                    e.Add($"{w}: snipe needs damage and bossDamage fractions in (0, 1]");
                if (wp.Kind == "accelerator" && (wp.Duration <= 0 || wp.Duration >= wp.Cooldown || wp.SpeedBonus <= 0))
                    e.Add($"{w}: accelerator needs positive speedBonus and duration shorter than cooldown");
                if (wp.Kind is "wood" or "aura" or "dropped" or "attackChain" && wp.Area <= 0)
                    e.Add($"{w}: {wp.Kind} needs area > 0");
                if (wp.Kind is "sustainedCone" or "dropped" && (wp.Duration <= 0 || wp.Duration >= wp.Cooldown))
                    e.Add($"{w}: {wp.Kind} needs a positive duration shorter than its cooldown");
                if (wp.Kind is "wood" or "aura" && (wp.Slow is <= 0 or >= 1 || wp.MinDamage < 0 || wp.MinDamage > wp.Damage))
                    e.Add($"{w}: {wp.Kind} needs slow in (0, 1) and minDamage between 0 and damage");
                if (wp.Kind is "rocket" or "attackChain" or "cane" && wp.Chance is <= 0 or > 1)
                    e.Add($"{w}: {wp.Kind} needs chance in (0, 1]");
                if (wp.Kind is "boomerang" or "bounce" or "rocket" && (wp.Speed <= 0 || wp.Range <= 0))
                    e.Add($"{w}: {wp.Kind} needs speed and range > 0");
                if (wp.Kind == "bolt" && (wp.Speed <= 0 || wp.Pierce < 1)) e.Add($"{w}: bolt needs speed > 0 and pierce >= 1");
                if (wp.Kind is "bolt" or "chain" or "blast" && wp.Range <= 0) e.Add($"{w}: {wp.Kind} needs range > 0");
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
            if (en.AttackSpeed < 0 || en.ShotRange < 0 || en.ShotSpeed < 0 ||
                en.DetonateRange < 0 || en.BlastRadius < 0 || en.CloudRadius < 0 || en.CloudSeconds < 0 || en.CloudDps < 0)
                e.Add($"{w}: attack and death effect parameters must be nonnegative");
            if ((en.ShotRange > 0 || en.ShotSpeed > 0) && (en.AttackSpeed <= 0 || en.ShotRange <= 0 || en.ShotSpeed <= 0))
                e.Add($"{w}: ranged attacks need positive attackSpeed, shotRange and shotSpeed");
            if (en.DetonateRange > 0 && en.BlastRadius < en.DetonateRange)
                e.Add($"{w}: blastRadius must cover detonateRange");
            if ((en.CloudRadius > 0 || en.CloudSeconds > 0 || en.CloudDps > 0) &&
                (en.CloudRadius <= 0 || en.CloudSeconds <= 0 || en.CloudDps <= 0))
                e.Add($"{w}: death clouds need positive cloudRadius, cloudSeconds and cloudDps");
        }
        if (!float.IsFinite(c.Waves.HpPerMinuteSquared) || c.Waves.HpPerMinuteSquared < 0)
            e.Add("waves.json: hpPerMinuteSquared must be finite and non-negative");

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
        foreach (var el in c.Waves.Elites)
        {
            if (c.EnemyIndex(el.Enemy) < 0) e.Add($"waves.json elite at {el.At}: unknown enemy '{el.Enemy}'");
            if (el.At < 0 || el.Count < 1) e.Add($"waves.json elite at {el.At}: needs at >= 0 and count >= 1");
        }
        var r = c.Rules;
        if (r.ArenaSize < 512) e.Add("rules.json: arenaSize must be >= 512");
        if (r.Chests.Count is < 0 or > TickHappens.Game.Sim.RunWorld.MaxChestSlots)
            e.Add("rules.json: invalid chest count");
        if (r.Chests.FreeCount < 0 || r.Chests.FreeCount + (r.Chests.RareFreeChest ? 1L : 0L) > r.Chests.Count)
            e.Add("rules.json: free chests exceed the chest count");
        if (c.Waves.BossIntervalMinutes < 0 || c.Waves.GoldPerMinute < 0)
            e.Add("waves.json: boss interval and gold growth must be nonnegative");
        if (r.Chests.PriceGrowth != 1.5m) e.Add("rules.json: chests.priceGrowth is fixed at 1.5 by the game design");
        foreach (var (rarity, weight) in r.Chests.RarityWeights)
        {
            if (!ContentSet.Rarities.Contains(rarity)) e.Add($"rules.json: chests.rarityWeights has unknown rarity '{rarity}'");
            if (!float.IsFinite(weight) || weight < 0) e.Add($"rules.json: rarity weight for '{rarity}' must be finite and nonnegative");
        }
        if (c.Items.Count > 0 && !c.Items.Any(i => r.Chests.RarityWeights.GetValueOrDefault(i.Rarity) > 0))
            e.Add("rules.json: chests.rarityWeights gives zero weight to every item rarity");
        CheckAttack("rules.json basicAttack", r.BasicAttack);
        if (r.Units.MoveSpeedScale <= 0 || r.Units.AttacksPerUnit <= 0 || r.Units.ResistCap <= 0 || r.Units.ResistCap >= 1)
            e.Add("rules.json: units need moveSpeedScale > 0, attacksPerUnit > 0 and 0 < resistCap < 1");
    }
}
