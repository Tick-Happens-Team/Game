using System.IO.Compression;
using System.Text;
using TickHappens.Distribution;

try
{
    switch (args)
    {
        case ["notes", var directory, var output]:
            // Validates patch notes and writes them as release-page Markdown (empty when there are none).
            WriteNew(output, NotesMarkdown(directory));
            return 0;
        case ["keygen", var privatePath, var publicPath]:
        {
            var keys = ManifestSigner.CreateKeyPair();
            WriteNew(privatePath, keys.PrivatePem);
            WriteNew(publicPath, keys.PublicPem);
            return 0;
        }
        case ["pack", var kind, var version, var commit, var source, var zipPath, var url]:
            Pack(kind, version, commit, source, zipPath, url);
            return 0;
        case ["channel", var sequence, var minimum, var game, var assets, var launcher, var publicKey, var output, .. var rest]
            when rest.Length <= 1:
        {
            string key = Environment.GetEnvironmentVariable("TICKHAPPENS_RELEASE_PRIVATE_KEY")
                ?? throw new InvalidOperationException("Set TICKHAPPENS_RELEASE_PRIVATE_KEY to the private PEM.");
            var manifest = new ChannelManifest
            {
                Sequence = long.Parse(sequence, System.Globalization.CultureInfo.InvariantCulture),
                Published = DateTimeOffset.UtcNow,
                MinLauncher = SemVer.Parse(minimum).ToString(),
                Game = new() { ["win-x64"] = ReadReference(game) },
                Assets = ReadReference(assets),
                Launcher = new() { ["win-x64"] = ReadReference(launcher) },
            };
            if (rest.Length == 1 && ReadNotes(rest[0]) is { } notes)
                foreach (var (language, text) in notes) manifest.SetNotes(language, text);
            if (manifest.Sequence < 1 || SemVer.Parse(manifest.Launcher["win-x64"].Version) < SemVer.Parse(minimum))
                throw new InvalidDataException("Invalid release sequence or launcher minimum.");
            string json = manifest.ToJson();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string signature = ManifestSigner.Sign(bytes, key);
            if (!ManifestSigner.Verify(bytes, signature, [File.ReadAllText(publicKey)]))
                throw new InvalidDataException("Signing key does not match the launcher public key.");
            WriteNew(output, json);
            WriteNew(output + ".sig", signature);
            return 0;
        }
    }
    Console.Error.WriteLine("""
        keygen PRIVATE.pem PUBLIC.pem
        pack game|assets|launcher VERSION COMMIT SOURCE ZIP URL
        channel SEQUENCE MIN_LAUNCHER GAME.zip.json ASSETS.zip.json LAUNCHER.zip.json PUBLIC.pem channel.json [NOTES_DIR]
          NOTES_DIR holds <language>.json patch notes (en.json, ru.json); see patchnotes/README.md.
        notes NOTES_DIR OUTPUT.md
        """);
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void Pack(string kind, string version, string commit, string source, string zipPath, string url)
{
    if (kind is not ("game" or "assets" or "launcher") || commit.Length != 40 || !commit.All(Uri.IsHexDigit))
        throw new ArgumentException("Expected a package kind and full source commit.");
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
        throw new ArgumentException("Expected an HTTPS package URL or a loopback test URL.");
    string root = Path.GetFullPath(source);
    if (Path.GetRelativePath(root, Path.GetFullPath(zipPath)).Split(Path.DirectorySeparatorChar)[0] != "..")
        throw new ArgumentException("Write the output archive outside the package source.");
    var manifest = PackageManifest.FromDirectory(source, kind, version);
    manifest.Channel = "stable";
    manifest.Commit = commit;
    manifest.Platform = kind == "assets" ? "any" : "win-x64";
    manifest.Format = kind == "assets" ? 1 : 0;
    if (kind == "game")
    {
        manifest.Entry = "TickHappens.Game.exe";
        manifest.Requires["assetsFormat"] = ">=1.0.0 <2.0.0";
    }
    if (kind == "launcher") manifest.Entry = "TickHappens.Launcher.exe";
    if (manifest.Entry is not null && !manifest.Files.Any(file => file.Path == manifest.Entry))
        throw new InvalidDataException($"Missing package executable {manifest.Entry}.");
    manifest.ValidatePaths();
    byte[] bytes = Encoding.UTF8.GetBytes(manifest.ToJson());
    using (var output = new FileStream(zipPath, FileMode.CreateNew))
    using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
    {
        using (var metadata = archive.CreateEntry(InstallLayout.ManifestFileName).Open()) metadata.Write(bytes);
        foreach (var entry in manifest.Files)
            archive.CreateEntryFromFile(Path.Combine(root, entry.Path), entry.Path);
    }
    var reference = new PackageRef
    {
        Version = version, Url = url, Size = new FileInfo(zipPath).Length,
        Sha256 = PackageManifest.Hash(zipPath),
        ManifestSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
    };
    WriteNew(zipPath + ".json", System.Text.Json.JsonSerializer.Serialize(reference, PackageManifest.Json));
}

static Dictionary<string, PatchNotes>? ReadNotes(string directory)
{
    var notes = new Dictionary<string, PatchNotes>(StringComparer.Ordinal);
    foreach (string file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
    {
        string language = Path.GetFileNameWithoutExtension(file);
        if (language.Length is < 2 or > 8 || !language.All(char.IsAsciiLetterLower))
            throw new InvalidDataException($"Patch note files are named by language code, not '{language}'.");
        try
        {
            var parsed = PatchNotes.FromJson(File.ReadAllText(file, Encoding.UTF8));
            if (!parsed.IsEmpty) notes[language] = parsed;
        }
        catch (System.Text.Json.JsonException error) { throw new InvalidDataException($"{file}: {error.Message}"); }
        catch (InvalidDataException error) { throw new InvalidDataException($"{file}: {error.Message}"); }
    }
    return notes.Count == 0 ? null : notes;
}

static string NotesMarkdown(string directory)
{
    var notes = ReadNotes(directory);
    if (notes is null) return "";
    var languages = notes.Keys.OrderBy(code => code == "en" ? 0 : 1).ThenBy(code => code, StringComparer.Ordinal).ToList();
    if (languages.Count == 1) return notes[languages[0]].ToMarkdown(ContentNames(languages[0]));
    var md = new StringBuilder();
    foreach (string code in languages)
        md.Append("## ").AppendLine(code switch { "en" => "English", "ru" => "Русский", _ => code })
            .AppendLine().AppendLine(notes[code].ToMarkdown(ContentNames(code)));
    return md.ToString();
}

// Localized hero, item and enemy names from the game's string tables, falling back to English.
static Func<PatchSection, string?> ContentNames(string language)
{
    var english = Strings("en");
    var own = language == "en" ? english : Strings(language);
    return section =>
    {
        string type = section.Kind switch { "hero" => "characters", "item" => "items", "enemy" => "enemies", _ => "" };
        string key = $"content.{type}.{section.Id}.name";
        return own.GetValueOrDefault(key) ?? english.GetValueOrDefault(key);
    };
}

static Dictionary<string, string> Strings(string language)
{
    using var stream = typeof(TickHappens.Localization.Localizer).Assembly.GetManifestResourceStream(language + ".json");
    return stream is null ? [] : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
}

static PackageRef ReadReference(string path) =>
    System.Text.Json.JsonSerializer.Deserialize<PackageRef>(File.ReadAllText(path), PackageManifest.Json)
    ?? throw new InvalidDataException("Missing package reference.");

static void WriteNew(string path, string content)
{
    using var file = new FileStream(path, FileMode.CreateNew);
    using var writer = new StreamWriter(file, new UTF8Encoding(false));
    writer.Write(content);
}
