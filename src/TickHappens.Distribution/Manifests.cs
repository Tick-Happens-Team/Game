using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickHappens.Distribution;

/// <summary>
/// Describes one installable package (game build for a platform, or an assets pack):
/// every file with size and SHA-256. Written by CI, verified by the launcher after download and before launch.
/// </summary>
public sealed class PackageManifest
{
    public const int SchemaVersion = 1;

    [JsonPropertyName("schema")] public int Schema { get; set; } = SchemaVersion;
    /// <summary>"game" | "assets" | "launcher".</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
    /// <summary>"stable" | "dev".</summary>
    [JsonPropertyName("channel")] public string Channel { get; set; } = "dev";
    /// <summary>Source commit the package was built from.</summary>
    [JsonPropertyName("commit")] public string Commit { get; set; } = "";
    /// <summary>"win-x64", "linux-x64", ... or "any" for assets.</summary>
    [JsonPropertyName("platform")] public string Platform { get; set; } = "any";
    /// <summary>Assets: the pack's content format (atlas/audio schema). Game: not used.</summary>
    [JsonPropertyName("format")] public int Format { get; set; }
    /// <summary>Game: accepted assets formats, e.g. "&gt;=1.0.0 &lt;2.0.0". Assets: unused.</summary>
    [JsonPropertyName("requires")] public Dictionary<string, string> Requires { get; set; } = [];
    /// <summary>Multiplayer protocol version of a game build (peers must match exactly).</summary>
    [JsonPropertyName("protocol")] public int Protocol { get; set; }
    /// <summary>Relative path of the executable to launch (game packages).</summary>
    [JsonPropertyName("entry")] public string? Entry { get; set; }
    [JsonPropertyName("files")] public List<FileEntry> Files { get; set; } = [];

    public long TotalSize => Files.Sum(f => f.Size);

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static PackageManifest FromJson(string json) =>
        JsonSerializer.Deserialize<PackageManifest>(json, Json) ?? throw new InvalidDataException("empty manifest");

    /// <summary>Builds a manifest from a directory: paths are relative with '/', sorted ordinally (deterministic).</summary>
    public static PackageManifest FromDirectory(string dir, string kind, string version)
    {
        SemVer.Parse(version);
        var m = new PackageManifest { Kind = kind, Version = version };
        string root = Path.GetFullPath(dir);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel == InstallLayout.ManifestFileName) continue;
            m.Files.Add(new FileEntry { Path = rel, Size = new FileInfo(file).Length, Sha256 = Hash(file) });
        }
        m.Files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return m;
    }

    public static string Hash(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    /// <summary>Paths must be relative, forward-slash, without "..", drive letters or reserved names (zip-slip / path traversal guard).</summary>
    public void ValidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Files)
        {
            string p = f.Path;
            if (p.Split('/').Any(UnsafeSegment) || p.Equals(InstallLayout.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"unsafe path in manifest: '{p}'");
            if (!seen.Add(p)) throw new InvalidDataException($"duplicate path (case-insensitive) in manifest: '{p}'");
            if (f.Size < 0 || f.Sha256.Length != 64) throw new InvalidDataException($"bad entry for '{p}'");
        }
    }

    private static bool UnsafeSegment(string segment)
    {
        if (segment.Length == 0 || segment.EndsWith('.') || segment.EndsWith(' ') ||
            segment.Any(c => c < 32 || "<>:\"\\|?*".Contains(c))) return true;
        string device = segment.Split('.')[0].TrimEnd().ToUpperInvariant();
        return device is "CON" or "NUL" or "PRN" or "AUX" or "CONIN$" or "CONOUT$" ||
            (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) &&
            "0123456789¹²³".Contains(device[3]));
    }
}

public sealed class FileEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

/// <summary>
/// A coherent, tested combination per channel ("release train"): exact game build + exact assets pack.
/// Published by CI (e.g. on GitHub Pages as channels/stable.json) together with a detached signature.
/// </summary>
public sealed class ChannelManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("channel")] public string Channel { get; set; } = "stable";
    /// <summary>Monotonic sequence number; the launcher refuses to roll back to a lower one (replay protection).</summary>
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
    [JsonPropertyName("published")] public DateTimeOffset Published { get; set; }
    /// <summary>Oldest launcher that understands this manifest; older launchers must self-update first.</summary>
    [JsonPropertyName("minLauncher")] public string MinLauncher { get; set; } = "0.1.0";
    [JsonPropertyName("game")] public Dictionary<string, PackageRef> Game { get; set; } = [];
    [JsonPropertyName("assets")] public PackageRef Assets { get; set; } = new();
    [JsonPropertyName("launcher")] public Dictionary<string, PackageRef> Launcher { get; set; } = [];
    /// <summary>
    /// Patch notes by language code ("en", "ru"), signed with the rest of the manifest. Kept as raw JSON so that a
    /// notes format this launcher does not understand hides the notes instead of rejecting the whole release.
    /// </summary>
    [JsonPropertyName("notes")] public Dictionary<string, JsonElement>? Notes { get; set; }

    public void SetNotes(string language, PatchNotes notes) =>
        (Notes ??= [])[language] = JsonSerializer.SerializeToElement(notes, PatchNotes.Json);

    /// <summary>Notes for a language, falling back to English and then to any language; null when there are none or they are unreadable.</summary>
    public PatchNotes? NotesFor(string language)
    {
        if (Notes is null || Notes.Count == 0) return null;
        var raw = Notes.TryGetValue(language, out var own) ? own : Notes.TryGetValue("en", out var english) ? english : Notes.Values.First();
        try
        {
            var notes = raw.Deserialize<PatchNotes>(PatchNotes.Json);
            notes?.Validate();
            return notes;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { return null; }
    }

    public static ChannelManifest FromJson(string json) =>
        JsonSerializer.Deserialize<ChannelManifest>(json, PackageManifest.Json) ?? throw new InvalidDataException("empty channel manifest");
    public string ToJson() => JsonSerializer.Serialize(this, PackageManifest.Json);
}

/// <summary>Points at a package archive and its manifest by URL and hash.</summary>
public sealed class PackageRef
{
    [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    /// <summary>SHA-256 of the package's manifest.json (inside the archive) - binds archive contents to the signed channel.</summary>
    [JsonPropertyName("manifestSha256")] public string ManifestSha256 { get; set; } = "";
}

public static class InstallLayout
{
    public const string ManifestFileName = "manifest.json";
}
