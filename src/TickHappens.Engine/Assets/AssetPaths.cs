namespace TickHappens.Engine.Assets;

/// <summary>Locates packaged assets and the media and shader directories used in development.</summary>
public static class AssetPaths
{
    private static string? _root;

    public static string Root => _root ??= Find();

    public static string Get(params string[] parts)
    {
        // Shaders ship with the executable and are developed in Game.
        if (parts.Length > 0 && parts[0] == "shaders")
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(Path.Combine(candidate, "shaders")))
                    return Path.Combine([candidate, .. parts]);
            }
        }
        return Path.Combine([Root, .. parts]);
    }

    public static string SourceRoot(string gameRoot) => Path.GetFullPath(
        Environment.GetEnvironmentVariable("TICKHAPPENS_ASSETS_ROOT") ?? Path.Combine(gameRoot, "..", "Assets"));

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "assets");
            if (File.Exists(Path.Combine(candidate, "atlas", "main.json"))) return candidate;
            if (File.Exists(Path.Combine(dir.FullName, "TickHappens.sln")))
            {
                candidate = Path.Combine(SourceRoot(dir.FullName), "assets");
                if (File.Exists(Path.Combine(candidate, "atlas", "main.json"))) return candidate;
            }
        }
        throw new DirectoryNotFoundException("assets/ not found; run the asset tool build first");
    }
}
