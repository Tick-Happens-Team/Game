using System.IO.Compression;
using System.Text;
using TickHappens.Distribution;

try
{
    switch (args)
    {
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
        case ["channel", var sequence, var minimum, var game, var assets, var launcher, var publicKey, var output]:
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
        channel SEQUENCE MIN_LAUNCHER GAME.zip.json ASSETS.zip.json LAUNCHER.zip.json PUBLIC.pem channel.json
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

static PackageRef ReadReference(string path) =>
    System.Text.Json.JsonSerializer.Deserialize<PackageRef>(File.ReadAllText(path), PackageManifest.Json)
    ?? throw new InvalidDataException("Missing package reference.");

static void WriteNew(string path, string content)
{
    using var file = new FileStream(path, FileMode.CreateNew);
    using var writer = new StreamWriter(file, new UTF8Encoding(false));
    writer.Write(content);
}
