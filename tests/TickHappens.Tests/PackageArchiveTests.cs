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

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
