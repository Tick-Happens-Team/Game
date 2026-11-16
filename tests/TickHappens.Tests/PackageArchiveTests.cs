using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TickHappens.Distribution;

namespace TickHappens.Tests;

public sealed class PackageArchiveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tickhappens-package-").FullName;

    [Theory]
    [InlineData("../outside")]
    [InlineData("some/CON.txt")]
    [InlineData("some/file. ")]
    [InlineData("some/file:stream")]
    public void RejectsUnsafeManifestPaths(string path)
    {
        var package = new PackageManifest { Files = [new() { Path = path, Sha256 = new string('0', 64) }] };
        Assert.Throws<InvalidDataException>(package.ValidatePaths);
    }

    [Fact]
    public void ExtractsVerifiedPackageAndRejectsTamperedArchive()
    {
        string zipPath = Path.Combine(_root, "package.zip");
        byte[] body = Encoding.UTF8.GetBytes("verified file");
        var manifest = new PackageManifest
        {
            Kind = "assets", Version = "0.1.0", Channel = "stable", Platform = "any",
            Files = [new() { Path = "assets/file.txt", Size = body.Length, Sha256 = Hash(body) }],
        };
        byte[] metadata = Encoding.UTF8.GetBytes(manifest.ToJson());
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var file = zip.CreateEntry(InstallLayout.ManifestFileName).Open()) file.Write(metadata);
            using (var file = zip.CreateEntry("assets/file.txt").Open()) file.Write(body);
        }
        var reference = new PackageRef
        {
            Version = manifest.Version, ManifestSha256 = Hash(metadata),
            Size = new FileInfo(zipPath).Length, Sha256 = PackageManifest.Hash(zipPath),
        };
        string destination = Path.Combine(_root, "install");
        PackageArchive.Extract(zipPath, reference, destination, "assets", "any", "stable", out var verified);
        Assert.Equal(metadata, verified);
        Assert.Equal(body, File.ReadAllBytes(Path.Combine(destination, "assets/file.txt")));

        using (var file = File.OpenWrite(zipPath)) file.WriteByte(0);
        Assert.Throws<InvalidDataException>(() =>
            PackageArchive.Extract(zipPath, reference, destination, "assets", "any", "stable", out _));
    }

    [Fact]
    public void PatchNotesAreSignedAndFallBackToEnglish()
    {
        const string Json = """
            {
              // Comments and trailing commas are allowed in hand-written notes.
              "general": [{ "text": "Chests open faster", "change": "buff" }],
              "sections": [{
                "kind": "hero", "id": "vampire",
                "changes": [{ "text": "Base damage reduced by 1", "change": "nerf", "details": ["Level 1 damage 10 to 9"] }],
                "groups": [{ "title": "Passive", "entries": [{ "name": "Blood pact", "changes": [{ "text": "Heals 5%" }] }] }],
              }],
            }
            """;
        var english = PatchNotes.FromJson(Json);
        var russian = PatchNotes.FromJson(Json.Replace("Chests open faster", "Сундуки открываются быстрее"));
        var keys = ManifestSigner.CreateKeyPair();
        var release = new ChannelManifest { Sequence = 2 };
        release.SetNotes("en", english);
        release.SetNotes("ru", russian);
        byte[] signed = Encoding.UTF8.GetBytes(release.ToJson());
        string signature = ManifestSigner.Sign(signed, keys.PrivatePem);

        var read = ChannelManifest.FromJson(Encoding.UTF8.GetString(signed));
        Assert.Equal("Сундуки открываются быстрее", read.NotesFor("ru")!.General[0].Text);
        Assert.Equal("Chests open faster", read.NotesFor("de")!.General[0].Text);
        Assert.Equal("Level 1 damage 10 to 9", read.NotesFor("en")!.Sections[0].Changes[0].Details[0]);
        Assert.Null(new ChannelManifest().NotesFor("en"));
        Assert.Contains("### vampire", english.ToMarkdown());
        Assert.Contains("- ▼ Base damage reduced by 1", english.ToMarkdown());

        read.SetNotes("en", PatchNotes.FromJson(Json.Replace("Chests open faster", "Free gold")));
        Assert.False(ManifestSigner.Verify(Encoding.UTF8.GetBytes(read.ToJson()), signature, [keys.PublicPem]));

        // A future notes format hides the notes but keeps the release readable.
        var future = ChannelManifest.FromJson("""{ "sequence": 3, "notes": { "en": ["old", "format"], "ru": { "general": 5 } } }""");
        Assert.Equal(3, future.Sequence);
        Assert.Null(future.NotesFor("en"));
        Assert.Null(future.NotesFor("ru"));
        Assert.True(ManifestSigner.Verify(signed, signature, [keys.PublicPem]));
    }

    [Fact]
    public void VersionHistoryIsCarriedForwardNewestFirst()
    {
        static ChannelManifest Release(long sequence, string text)
        {
            var release = new ChannelManifest
            {
                Sequence = sequence, Published = new DateTimeOffset(2026, 11, (int)sequence, 12, 0, 0, TimeSpan.Zero),
                Game = new() { ["win-x64"] = new PackageRef { Version = $"0.1.{sequence}" } },
            };
            release.SetNotes("en", PatchNotes.FromJson($$"""{ "general": [{ "text": "{{text}}" }] }"""));
            return release;
        }
        var first = Release(1, "First");
        var second = Release(2, "Second");
        second.CarryHistory(first);
        var third = Release(3, "Third");
        third.CarryHistory(ChannelManifest.FromJson(second.ToJson()));

        var read = ChannelManifest.FromJson(third.ToJson());
        Assert.Equal([3L, 2, 1], read.Versions().Select(version => version.Sequence));
        Assert.Equal(["0.1.3", "0.1.2", "0.1.1"], read.Versions().Select(version => version.Version));
        Assert.Equal("First", read.Versions()[2].NotesFor("ru")!.General[0].Text);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero), read.Versions()[1].Published);

        // A forged entry at or above the release's own sequence is dropped when carried and when shown.
        second.History!.Add(new ReleaseHistoryEntry { Sequence = 9, Version = "9.9.9" });
        third.CarryHistory(second);
        Assert.Equal([2L, 1], third.History!.Select(entry => entry.Sequence));
        read.History!.Insert(0, new ReleaseHistoryEntry { Sequence = 3, Version = "fake" });
        Assert.Equal(["0.1.3", "0.1.2", "0.1.1"], read.Versions().Select(version => version.Version));
        Assert.Throws<InvalidDataException>(() => first.CarryHistory(second));

        // History is part of the signed manifest.
        var keys = ManifestSigner.CreateKeyPair();
        byte[] signed = Encoding.UTF8.GetBytes(third.ToJson());
        string signature = ManifestSigner.Sign(signed, keys.PrivatePem);
        third.History![0].Version = "0.0.1";
        Assert.False(ManifestSigner.Verify(Encoding.UTF8.GetBytes(third.ToJson()), signature, [keys.PublicPem]));

        // Old launchers that do not know the field still read the release.
        Assert.Equal(3, System.Text.Json.JsonSerializer.Deserialize<ChannelManifest>(signed)!.Sequence);
    }

    [Fact]
    public void VersionHistoryDropsOldNotesBeyondBudgetButKeepsVersions()
    {
        string line = new('x', 290);
        string big = $$"""{ "general": [{{string.Join(",", Enumerable.Repeat($$"""{ "text": "{{line}}" }""", 390))}}] }""";
        var previous = new ChannelManifest { Sequence = 1, Game = new() { ["win-x64"] = new PackageRef { Version = "0.1.1" } } };
        for (long sequence = 2; sequence <= 12; sequence++)
        {
            var next = new ChannelManifest { Sequence = sequence, Game = new() { ["win-x64"] = new PackageRef { Version = $"0.1.{sequence}" } } };
            next.SetNotes("en", PatchNotes.FromJson(big));
            next.SetNotes("ru", PatchNotes.FromJson(big));
            next.CarryHistory(previous);
            previous = next;
        }
        Assert.Equal(11, previous.History!.Count);
        Assert.NotNull(previous.History[0].Notes);
        Assert.Null(previous.History[^2].Notes);
        Assert.True(Encoding.UTF8.GetByteCount(previous.ToJson()) < PackageArchive.MaxManifestBytes);
    }

    [Theory]
    [InlineData("""{ "sections": [{ "kind": "weapon", "id": "x" }] }""")]
    [InlineData("""{ "general": [{ "text": "x", "change": "maybe" }] }""")]
    [InlineData("""{ "general": [{ "text": "" }] }""")]
    [InlineData("{ \"general\": [{ \"text\": \"two\nlines\" }] }")]
    [InlineData("""{ "sections": [{ "kind": "item" }] }""")]
    [InlineData("""{ "typo": [] }""")]
    public void RejectsInvalidPatchNotes(string json) =>
        Assert.ThrowsAny<Exception>(() => PatchNotes.FromJson(json));

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
