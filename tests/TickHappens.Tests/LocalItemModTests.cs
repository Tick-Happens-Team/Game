using TickHappens.Game.Content;

namespace TickHappens.Tests;

public sealed class LocalItemModTests
{
    [Fact]
    public void ProfileControlsItemsLoadedByTheGame()
    {
        var directory = Directory.CreateTempSubdirectory("tickhappens-local-mod-");
        try
        {
            string mod = Directory.CreateDirectory(Path.Combine(directory.FullName, "example")).FullName;
            File.WriteAllText(Path.Combine(mod, "items.json"), """
                [{"id":"example:boots","name":"Example boots","mods":[{"stat":"moveSpeed","add":3}]}]
                """);
            string profile = Path.Combine(directory.FullName, "enabled-mods.json");
            string core = ContentLoader.FindCore();
            File.WriteAllText(profile, """["example"]""");
            Assert.Contains(ContentLoader.Load(core, directory.FullName, profile).Items,
                item => item.Id == "example:boots");
            File.WriteAllText(profile, "[]");
            Assert.DoesNotContain(ContentLoader.Load(core, directory.FullName, profile).Items,
                item => item.Id == "example:boots");
            File.WriteAllText(profile, """["../example"]""");
            Assert.Throws<ContentException>(() => ContentLoader.Load(core, directory.FullName, profile));
        }
        finally { directory.Delete(recursive: true); }
    }
}
