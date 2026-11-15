using TickHappens.Game.Content;

namespace TickHappens.Tests;

public sealed class ContentLoaderTests
{
    private static readonly string[] CharacterIds =
        ["octarine", "swordsman", "smith", "goflin", "celestial_mage", "watchman", "nekochan"];

    [Fact]
    public void CoreContentLoadsWithExpectedShape()
    {
        var content = TestContent.LoadCore();

        Assert.Equal(CharacterIds.Length, content.Characters.Count);
        Assert.Equal(62, content.Items.Count);
        Assert.Equal(3, content.Enemies.Count);
        Assert.NotEmpty(content.Waves.Waves);
        Assert.Equal(1.5m, content.Rules.Chests.PriceGrowth);
        Assert.Equal(0.9f, content.Rules.Units.ResistCap);
        Assert.True(content.Rules.Statue.Count > 0, "core needs statues");
        Assert.True(content.Rules.Chests.Count > 0, "core needs chests");

        foreach (var wave in content.Waves.Waves)
        {
            Assert.NotEmpty(wave.Mix);
            foreach (string enemy in wave.Mix.Keys)
                Assert.True(content.EnemyIndex(enemy) >= 0,
                    $"wave {wave.From}-{wave.To} mixes unknown enemy '{enemy}'");
        }

        foreach (var elite in content.Waves.Elites)
            Assert.True(content.EnemyIndex(elite.Enemy) >= 0,
                $"elite at {elite.At} uses unknown enemy '{elite.Enemy}'");

        foreach (var item in content.Items)
        {
            Assert.Contains(item.Rarity, ContentSet.Rarities.Append(ContentSet.CursedRarity));
            Assert.True(item.Mods.Count > 0 || item.Weapon != null || item.XpOnPickup > 0 || item.Companion != null || item.Effect != null, $"item '{item.Id}' needs mods, a weapon or pickup XP");
        }
    }

    [Fact]
    public void CoreCharactersMatchTheDesignerTable()
    {
        var content = TestContent.LoadCore();

        Assert.Equal(CharacterIds, content.Characters.Select(c => c.Id).ToArray());

        var octarine = Character(content, "octarine");
        Assert.Equal(150f, octarine.Stats.MaxHp);
        Assert.Equal(5f, octarine.Stats.MaxHpPerLevel);
        Assert.Equal(0.05f, octarine.Stats.Armor);
        Assert.Equal(0.10f, octarine.Stats.MagicResist);
        Assert.Equal(15f, octarine.Stats.Damage);
        Assert.Equal(2.75f, octarine.Stats.DamagePerLevel);
        var cooldown = Assert.Single(octarine.Passives);
        Assert.Equal("cooldown", cooldown.Stat);
        Assert.Equal(-0.10f, cooldown.Pct);
        Assert.Equal(-0.0015f, cooldown.PerLevel!.Pct);

        var swordsman = Character(content, "swordsman");
        Assert.Equal(18f, swordsman.Stats.MoveSpeed);
        Assert.Equal(0.10f, swordsman.Stats.Armor);
        Assert.Equal(0.01f, Assert.Single(swordsman.Passives).PerLevel!.Add);

        var mage = Character(content, "celestial_mage");
        Assert.NotNull(mage.Attack);
        Assert.Equal("magical", mage.Attack.DamageType);
        Assert.True(ContentLoader.TryParseDamage(mage.Attack.DamageType, out var kind));
        Assert.Equal(DamageKind.Magical, kind);

        var watchman = Character(content, "watchman");
        Assert.Equal(50f, watchman.Stats.AttackSpeed);
        var perMinute = Assert.Single(watchman.Passives);
        Assert.Equal("runMinute", perMinute.Per);
        Assert.Equal(0.75f, perMinute.Add);
        Assert.Equal(0.25f, perMinute.PerLevel!.Add);

        var nekochan = Character(content, "nekochan");
        Assert.Equal(175f, nekochan.Stats.MaxHp);
        Assert.Equal(3.5f, nekochan.Stats.MaxHpPerLevel);
        Assert.Equal(0.15f, nekochan.Stats.Regen);
        Assert.Equal(45f, nekochan.Stats.MoveSpeed);
        Assert.Equal(35f, nekochan.Stats.AttackSpeed);
        Assert.Equal(0.025f, nekochan.Stats.Armor);
        Assert.Equal(0.025f, nekochan.Stats.MagicResist);
        Assert.Equal(15f, nekochan.Stats.Damage);
        Assert.Equal(1.5f, nekochan.Stats.DamagePerLevel);
        var aura = Assert.IsType<CharacterAura>(nekochan.Aura);
        Assert.Equal(200f, aura.Radius);
        Assert.Equal(1.25f, aura.RadiusPerLevel);
        Assert.Equal(0.08f, aura.DamageReduction);
        Assert.Equal(0.003f, aura.DamageReductionPerLevel);
        Assert.Equal(1.75f, aura.Regen);
        Assert.Equal(0.25f, aura.RegenPerLevel);
        Assert.Equal(1.75f, aura.SelfMultiplier);
    }

    [Fact]
    public void ScratchCopyOfCoreLoadsCleanly()
    {
        string dir = TestContent.CreateScratchCopy();
        try
        {
            var copy = ContentLoader.Load(dir);
            var core = TestContent.LoadCore();
            Assert.Equal(core.Characters.Count, copy.Characters.Count);
            Assert.Equal(core.Items.Count, copy.Items.Count);
            Assert.Equal(core.Enemies.Count, copy.Enemies.Count);
            Assert.Equal(core.Waves.Waves.Count, copy.Waves.Waves.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UnknownPassiveStatIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "characters.json", root =>
            root.AsArray()[0]!["passives"]!.AsArray()[0]!["stat"] = "bogus_stat"));

        Assert.Contains(errors, e => e.Contains("unknown stat 'bogus_stat'", StringComparison.Ordinal));
    }

    [Fact]
    public void PassivePerOtherThanRunMinuteIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "characters.json", root =>
            root.AsArray()[0]!["passives"]!.AsArray()[0]!["per"] = "runSecond"));

        Assert.Contains(errors, e => e.Contains("passive 'per' must be omitted or 'runMinute'", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownItemRarityIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "items.json", root =>
            root.AsArray()[0]!["rarity"] = "ultra"));

        Assert.Contains(errors, e => e.Contains("rarity 'ultra' must be one of", StringComparison.Ordinal));
    }

    [Fact]
    public void ArmorAtOrAboveOneIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "characters.json", root =>
            root.AsArray()[0]!["stats"]!["armor"] = 1.0));

        Assert.Contains(errors, e => e.Contains("armor", StringComparison.Ordinal) &&
                                     e.Contains("[0, 1)", StringComparison.Ordinal));
    }

    [Fact]
    public void NonStandardChestPriceGrowthIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "rules.json", root =>
            root["chests"]!["priceGrowth"] = 2.0));

        Assert.Contains(errors, e => e.Contains("priceGrowth", StringComparison.Ordinal));
    }

    [Fact]
    public void WaveGapIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "waves.json", root =>
            root["waves"]!.AsArray()[0]!["to"] = 0.5));

        Assert.Contains(errors, e => e.Contains("gaps", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateIdsAreReported()
    {
        string duplicateId = TestContent.LoadCore().Items[0].Id;

        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "items.json", root =>
        {
            var items = root.AsArray();
            items.Add(items[0]!.DeepClone());
        }));

        Assert.Contains(errors, e => e.Contains($"duplicate id '{duplicateId}'", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownRarityWeightIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "rules.json", root =>
            root["chests"]!["rarityWeights"]!["ultra"] = 5));

        Assert.Contains(errors, e => e.Contains("unknown rarity 'ultra'", StringComparison.Ordinal));
    }

    private static CharacterDef Character(ContentSet content, string id) =>
        content.Characters[content.CharacterIndex(id)];
}
