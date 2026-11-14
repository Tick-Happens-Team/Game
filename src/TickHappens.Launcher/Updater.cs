using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TickHappens.Distribution;

namespace TickHappens.Launcher;

internal sealed record Install(string Directory, string Manifest, string Signature);

internal sealed class Updater(string root, Uri channelUrl, string publicKey)
{
    public const string Platform = "win-x64";
    private const string Channel = "stable";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    public static readonly SemVer Version = SemVer.Parse(
        typeof(Updater).Assembly.GetName().Version!.ToString(3));

    public FileStream Lock() => new(Path.Combine(root, "launcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    public async Task<string?> UpdateAsync(Action<string> status, CancellationToken token)
    {
        status("Checking for updates…");
        if (publicKey.Length == 0) throw new InvalidOperationException("This launcher build has no embedded release public key.");
        string manifest = await ReadRemoteAsync(channelUrl, PackageArchive.MaxManifestBytes, token);
        string signature = await ReadRemoteAsync(new(channelUrl.AbsoluteUri + ".sig"), 1024, token);
        var release = Verify(manifest, signature);
        var currentGame = ReadInstall("game");
        var currentLauncher = ReadInstall("launcher");
        if (release.Sequence < Math.Max(currentGame is null ? 0 : Verify(currentGame).Sequence,
            currentLauncher is null ? 0 : Verify(currentLauncher).Sequence))
            throw new InvalidDataException("The server returned an older release sequence.");
        if ((currentGame is not null && Verify(currentGame).Sequence == release.Sequence && currentGame.Manifest != manifest) ||
            (currentLauncher is not null && Verify(currentLauncher).Sequence == release.Sequence && currentLauncher.Manifest != manifest))
            throw new InvalidDataException("The server reused a release sequence.");

        if (release.Launcher.TryGetValue(Platform, out var launcher) && SemVer.Parse(launcher.Version) > Version)
        {
            if (SemVer.Parse(launcher.Version) < SemVer.Parse(release.MinLauncher))
                throw new InvalidDataException("Launcher package does not meet the release minimum version.");
            string candidate = await InstallAsync("launcher", manifest, signature, [("launcher", Platform, launcher)], status, token);
            return candidate;
        }
        if (Version < SemVer.Parse(release.MinLauncher))
            throw new InvalidDataException("Download the latest launcher from GitHub Releases.");
        var game = release.Game[Platform];
        if (currentGame?.Manifest == manifest)
        {
            try { await Task.Run(() => Entry(currentGame, "game"), token); return null; }
            catch (Exception error) when (error is IOException or InvalidDataException) { }
        }
        await InstallAsync("game", manifest, signature, [("game", Platform, game), ("assets", "any", release.Assets)], status, token);
        return null;
    }

    public string? CurrentEntry(string kind)
    {
        var install = ReadInstall(kind);
        if (install is null) return null;
        if (kind == "launcher" && SemVer.Parse(Verify(install).Launcher[Platform].Version) <= Version) return null;
        return Entry(install, kind);
    }

    private string Entry(Install install, string kind)
    {
        var release = Verify(install);
        if (!Guid.TryParseExact(install.Directory, "N", out _)) throw new InvalidDataException("Invalid install directory.");
        string folder = Path.Combine(root, "installs", install.Directory);
        RejectLinks(folder);
        var payload = VerifyInstalled(folder, kind, kind == "game" ? release.Game[Platform] : release.Launcher[Platform]);
        var expected = payload.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        expected.Add(kind + ".manifest.json");
        if (kind == "game")
        {
            var assets = VerifyInstalled(folder, "assets", release.Assets);
            expected.UnionWith(assets.Files.Select(file => file.Path));
            expected.Add("assets.manifest.json");
        }
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            if (!expected.Contains(Path.GetRelativePath(folder, file).Replace('\\', '/')))
                throw new InvalidDataException("The installation contains unexpected files.");
        return Executable(folder, payload);
    }

    private static void RejectLinks(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The installation contains a filesystem link.");
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The installation contains a filesystem link.");
            if ((attributes & FileAttributes.Directory) != 0) RejectLinks(path);
        }
    }

    private static PackageManifest VerifyInstalled(string folder, string kind, PackageRef reference)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, kind + ".manifest.json"));
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(reference.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed manifest hash mismatch.");
        var manifest = PackageManifest.FromJson(Encoding.UTF8.GetString(bytes));
        if (Integrity.Verify(folder, manifest, reportExtra: false).Count != 0)
            throw new InvalidDataException("Installed files are damaged. Reconnect to repair the installation.");
        return manifest;
    }

    private async Task<string> InstallAsync(string kind, string manifest, string signature,
        (string Kind, string Platform, PackageRef Ref)[] packages, Action<string> status, CancellationToken token)
    {
        var release = Verify(manifest, signature);
        var install = new Install(Guid.NewGuid().ToString("N"), manifest, signature);
        string folder = Path.Combine(root, "installs", install.Directory);
        Directory.CreateDirectory(folder);
        bool committed = false;
        try
        {
            PackageManifest? payload = null;
            foreach (var package in packages)
            {
                status($"Installing {package.Kind} {package.Ref.Version}…");
                string archive = await DownloadAsync(package.Ref, token);
                var extracted = await Task.Run(() =>
                {
                    var packageManifest = PackageArchive.Extract(archive, package.Ref, folder,
                        package.Kind, package.Platform, Channel, out var bytes);
                    return (Manifest: packageManifest, Bytes: bytes);
                }, token);
                using (var metadata = new FileStream(Path.Combine(folder, package.Kind + ".manifest.json"), FileMode.CreateNew))
                    metadata.Write(extracted.Bytes);
                if (package.Kind == kind) payload = extracted.Manifest;
                if (package.Kind == "assets")
                {
                    var errors = Compatibility.Check(release, payload!, extracted.Manifest, Version);
                    if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                }
            }
            string entry = await Task.Run(() => Entry(install, kind), token);
            token.ThrowIfCancellationRequested();
            string pointer = Path.Combine(root, kind + ".json");
            using (var pending = new FileStream(pointer + ".new", FileMode.Create))
            {
                JsonSerializer.Serialize(pending, install);
                pending.Flush(flushToDisk: true);
            }
            File.Move(pointer + ".new", pointer, overwrite: true);
            committed = true;
            return entry;
        }
        finally
        {
            if (!committed) Directory.Delete(folder, recursive: true);
        }
    }

    private static string Executable(string folder, PackageManifest manifest)
    {
        if (manifest.Entry is not { } entry || !entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !manifest.Files.Any(file => file.Path == entry))
            throw new InvalidDataException("The package has no valid executable entry.");
        return Path.Combine(folder, entry);
    }

    private Install? ReadInstall(string kind)
    {
        string file = Path.Combine(root, kind + ".json");
        return File.Exists(file) ? JsonSerializer.Deserialize<Install>(File.ReadAllText(file)) : null;
    }

    private ChannelManifest Verify(Install install) => Verify(install.Manifest, install.Signature);

    private ChannelManifest Verify(string manifest, string signature)
    {
        if (!ManifestSigner.Verify(Encoding.UTF8.GetBytes(manifest), signature, [publicKey]))
            throw new InvalidDataException("Release signature is invalid.");
        var release = ChannelManifest.FromJson(manifest);
        if (release.Schema != 1 || release.Channel != Channel || release.Sequence < 1 || !release.Game.ContainsKey(Platform))
            throw new InvalidDataException("Unsupported release channel.");
        return release;
    }

    private async Task<string> DownloadAsync(PackageRef package, CancellationToken token)
    {
        if (package.Size is <= 0 or > PackageArchive.MaxPackageBytes ||
            package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid signed package size or digest.");
        string cache = Path.Combine(root, "downloads");
        Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, package.Sha256.ToLowerInvariant() + ".zip");
        if (File.Exists(archive) && new FileInfo(archive).Length == package.Size &&
            await Task.Run(() => PackageManifest.Hash(archive).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase), token))
            return archive;
        var uri = new Uri(package.Url);
        string temp = archive + ".part";
        try
        {
            await using (var file = File.Create(temp))
                await DownloadToAsync(uri, file, package.Size, token);
            if (new FileInfo(temp).Length != package.Size ||
                !await Task.Run(() => PackageManifest.Hash(temp).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase), token))
                throw new InvalidDataException("Downloaded archive does not match the signed release.");
            File.Move(temp, archive, overwrite: true);
            return archive;
        }
        finally { File.Delete(temp); }
    }

    private static async Task<string> ReadRemoteAsync(Uri uri, int limit, CancellationToken token)
    {
        using var stream = new MemoryStream();
        await DownloadToAsync(uri, stream, limit, token);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task DownloadToAsync(Uri uri, Stream output, long limit, CancellationToken token)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new InvalidDataException("Release downloads require HTTPS.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        byte[] buffer = new byte[65536];
        int count;
        long total = 0;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Release download exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
        }
    }
}
