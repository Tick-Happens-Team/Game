using TickHappens.Game.Content;

namespace TickHappens.Tests;

public sealed class ContentLoaderTests
{
    [Fact]
    public void CoreContentLoadsWithExpectedShape()
    {
        var content = TestContent.LoadCore();

        Assert.True(content.Characters.Count >= 1, "core needs at least one character");
        Assert.True(content.Weapons.Count >= 4, $"core needs at least 4 weapons, found {content.Weapons.Count}");
        Assert.True(content.Items.Count >= 6, $"core needs at least 6 items, found {content.Items.Count}");
        Assert.True(content.Enemies.Count >= 3, $"core needs at least 3 enemies, found {content.Enemies.Count}");
        Assert.NotEmpty(content.Waves.Waves);

        foreach (var character in content.Characters)
            Assert.True(content.WeaponIndex(character.Weapon) >= 0,
                $"character '{character.Id}' starts with unknown weapon '{character.Weapon}'");

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

        Assert.Equal(1.5m, content.Rules.Chests.PriceGrowth);
        Assert.True(content.Rules.Statue.Count > 0, "core needs statues");
        Assert.True(content.Rules.Chests.Count > 0, "core needs chests");
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
            Assert.Equal(core.Weapons.Count, copy.Weapons.Count);
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
    public void UnknownStartingWeaponIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "characters.json", root =>
            root.AsArray()[0]!["weapon"] = "missing_weapon"));

        Assert.Contains(errors, e => e.Contains("unknown starting weapon 'missing_weapon'", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownItemStatIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "items.json", root =>
            root.AsArray()[0]!["mods"]!.AsArray()[0]!["stat"] = "bogus_stat"));

        Assert.Contains(errors, e => e.Contains("unknown stat 'bogus_stat'", StringComparison.Ordinal));
    }

    [Fact]
    public void WaveGapIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "waves.json", root =>
            root["waves"]!.AsArray()[0]!["to"] = 0.5));

        Assert.Contains(errors, e => e.Contains("gaps", StringComparison.Ordinal));
    }

    [Fact]
    public void NonStandardChestPriceGrowthIsReported()
    {
        var errors = TestContent.LoadErrors(dir => TestContent.EditJson(dir, "rules.json", root =>
            root["chests"]!["priceGrowth"] = 2.0));

        Assert.Contains(errors, e => e.Contains("priceGrowth", StringComparison.Ordinal));
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
}
