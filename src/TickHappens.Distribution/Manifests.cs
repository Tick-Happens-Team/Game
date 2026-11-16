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

    /// <summary>
    /// Earlier releases of this channel, newest first, carried forward by the release tool from the previous signed
    /// manifest so the launcher can show the version history without another download.
    /// </summary>
    [JsonPropertyName("history")] public List<ReleaseHistoryEntry>? History { get; set; }

    /// <summary>Releases kept in <see cref="History"/>, and the most bytes of notes they may carry.</summary>
    public const int MaxHistory = 200, MaxHistoryNotesBytes = 1 << 20;

    public void SetNotes(string language, PatchNotes notes) =>
        (Notes ??= [])[language] = JsonSerializer.SerializeToElement(notes, PatchNotes.Json);

    /// <summary>Notes for a language, falling back to English and then to any language; null when there are none or they are unreadable.</summary>
    public PatchNotes? NotesFor(string language) => PatchNotes.Pick(Notes, language);

    /// <summary>This release as a history entry.</summary>
    public ReleaseHistoryEntry ToHistoryEntry() => new()
    {
        Sequence = Sequence, Published = Published, Notes = Notes,
        Version = Game.TryGetValue("win-x64", out var game) ? game.Version : Game.Values.FirstOrDefault()?.Version ?? "",
    };

    /// <summary>
    /// Sets <see cref="History"/> from earlier signed releases and the histories they carry: only lower sequences,
    /// newest first, one entry per sequence (a release's own manifest wins over a copy carried by a later one), at most
    /// <see cref="MaxHistory"/> entries. Older entries lose their notes once the notes budget is spent, so the manifest
    /// stays small while the version list stays complete. Passing every published release backfills releases that
    /// were published before manifests carried a history.
    /// </summary>
    public void CarryHistory(params IEnumerable<ChannelManifest> previous)
    {
        var releases = previous.ToList();
        if (releases.Any(release => release.Sequence >= Sequence))
            throw new InvalidDataException("Earlier releases must have a lower sequence.");
        var bySequence = new Dictionary<long, ReleaseHistoryEntry>();
        foreach (var entry in releases.Select(release => release.ToHistoryEntry())
            .Concat(releases.SelectMany(release => release.History ?? [])))
            if (entry.Sequence >= 1 && entry.Sequence < Sequence) bySequence.TryAdd(entry.Sequence, entry);
        var history = new List<ReleaseHistoryEntry>();
        long budget = MaxHistoryNotesBytes;
        foreach (var entry in bySequence.Values.OrderByDescending(entry => entry.Sequence).Take(MaxHistory))
        {
            var kept = new ReleaseHistoryEntry { Sequence = entry.Sequence, Version = entry.Version, Published = entry.Published, Notes = entry.Notes };
            if (kept.Notes is { Count: > 0 } notes)
            {
                long size = JsonSerializer.SerializeToUtf8Bytes(notes, PackageManifest.Json).LongLength;
                if (size > budget) kept.Notes = null;
                else budget -= size;
            }
            history.Add(kept);
        }
        History = history.Count == 0 ? null : history;
    }

    /// <summary>This release and its history, newest first, ignoring entries that are not older than this release.</summary>
    public List<ReleaseHistoryEntry> Versions()
    {
        var versions = new List<ReleaseHistoryEntry> { ToHistoryEntry() };
        var seen = new HashSet<long> { Sequence };
        foreach (var entry in History ?? [])
            if (entry.Sequence < Sequence && entry.Sequence >= 1 && seen.Add(entry.Sequence)) versions.Add(entry);
        versions.Sort((a, b) => b.Sequence.CompareTo(a.Sequence));
        return versions;
    }

    public static ChannelManifest FromJson(string json) =>
        JsonSerializer.Deserialize<ChannelManifest>(json, PackageManifest.Json) ?? throw new InvalidDataException("empty channel manifest");
    public string ToJson() => JsonSerializer.Serialize(this, PackageManifest.Json);
}

/// <summary>One earlier release in a channel's version history: its game version, date and patch notes.</summary>
public sealed class ReleaseHistoryEntry
{
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("published")] public DateTimeOffset Published { get; set; }
    /// <summary>Patch notes by language code as raw JSON, like <see cref="ChannelManifest.Notes"/>.</summary>
    [JsonPropertyName("notes")] public Dictionary<string, JsonElement>? Notes { get; set; }

    public PatchNotes? NotesFor(string language) => PatchNotes.Pick(Notes, language);
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
