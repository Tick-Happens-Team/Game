using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TickHappens.Engine.Ui;
using TickHappens.Localization;

namespace TickHappens.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("ru", "en", "ru")]
    [InlineData(null, "ru", "ru")]
    [InlineData("invalid", "fr", "en")]
    public void ResolvesPreferenceAndSystemFallback(string? preference, string system, string expected) =>
        Assert.Equal(expected, Localizer.ResolveLanguage(preference, system));

    [Fact]
    public void CatalogsHaveMatchingKeysAndFormatArgumentsAndCoverCoreContent()
    {
        var en = Load("en");
        var ru = Load("ru");
        Assert.Equal(en.Keys.Order(), ru.Keys.Order());
        foreach (string key in en.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]));
            Assert.False(string.IsNullOrWhiteSpace(ru[key]));
            Assert.Equal(CompositeFormat.Parse(en[key]).MinimumArgumentCount, CompositeFormat.Parse(ru[key]).MinimumArgumentCount);
        }
        var core = TestContent.LoadCore();
        foreach (var character in core.Characters)
            foreach (string field in new[] { "name", "passive", "growth" })
                Assert.Contains($"content.characters.{character.Id}.{field}", en.Keys);
        foreach (var item in core.Items)
            foreach (string field in new[] { "name", "description" })
                Assert.Contains($"content.items.{item.Id}.{field}", en.Keys);
        Assert.Equal("Mod name", Localizer.Content("items", "example:item", "name", "Mod name"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void InGameTextUsesOnlyPixelFontGlyphs(string code)
    {
        // Launcher text uses Windows fonts; the window title and console messages use system fonts.
        var missing = new List<string>();
        foreach (var (key, value) in Load(code))
        {
            if (key.StartsWith("launcher.", StringComparison.Ordinal) || key is "app.stats" or "language.saveError") continue;
            foreach (char c in Regex.Replace(value, @"\{\d+(:[^}]*)?\}", ""))
                if (!PixelFont.HasGlyph(c)) missing.Add($"{key}: '{c}' U+{(int)c:X4}");
        }
        Assert.Empty(missing);
    }

    private static Dictionary<string, string> Load(string code)
    {
        using var json = typeof(Localizer).Assembly.GetManifestResourceStream(code + ".json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }
}
