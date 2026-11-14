using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TickHappens.Distribution;

public static class PackageArchive
{
    public const long MaxPackageBytes = 8L << 30;
    public const int MaxManifestBytes = 4 << 20;

    public static PackageManifest Extract(string archive, PackageRef reference, string destination,
        string kind, string platform, string channel, out byte[] manifestBytes)
    {
        using var file = File.OpenRead(archive);
        if (file.Length != reference.Size || !Matches(SHA256.HashData(file), reference.Sha256))
            throw new InvalidDataException("Package archive integrity check failed.");
        file.Position = 0;
        using var zip = new ZipArchive(file);
        var metadata = zip.GetEntry(InstallLayout.ManifestFileName);
        if (metadata is null || metadata.Length > MaxManifestBytes || zip.Entries.Count > 65536)
            throw new InvalidDataException("Missing manifest or oversized archive.");
        using var stream = metadata.Open();
        byte[] bytes = new byte[(int)metadata.Length];
        manifestBytes = bytes;
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 || !Matches(SHA256.HashData(bytes), reference.ManifestSha256))
            throw new InvalidDataException("Package manifest integrity check failed.");
        var manifest = PackageManifest.FromJson(Encoding.UTF8.GetString(bytes));
        if (manifest.Schema != PackageManifest.SchemaVersion || manifest.Kind != kind ||
            manifest.Version != reference.Version || manifest.Platform != platform || manifest.Channel != channel)
            throw new InvalidDataException($"Incompatible {kind} package.");
        manifest.ValidatePaths();
        if (manifest.Files.Any(item => item.Size > MaxPackageBytes) || manifest.TotalSize > MaxPackageBytes ||
            zip.Entries.Count != manifest.Files.Count + 1)
            throw new InvalidDataException("Unexpected package size or entry count.");
        var expected = manifest.Files.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        byte[] buffer = new byte[65536];
        foreach (var entry in zip.Entries)
        {
            if (ReferenceEquals(entry, metadata)) continue;
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                !expected.Remove(entry.FullName, out var item) || entry.Length != item.Size)
                throw new InvalidDataException($"Unexpected archive entry '{entry.FullName}'.");
            string path = Path.Combine(destination, item.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using (var output = new FileStream(path, FileMode.CreateNew))
            {
                long remaining = item.Size;
                while (remaining > 0)
                {
                    int length = (int)Math.Min(buffer.Length, remaining);
                    source.ReadExactly(buffer.AsSpan(0, length));
                    output.Write(buffer, 0, length);
                    remaining -= length;
                }
                if (source.ReadByte() != -1) throw new InvalidDataException("Archive entry exceeds declared size.");
            }
            if (!string.Equals(PackageManifest.Hash(path), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid file hash: '{item.Path}'.");
        }
        return manifest;
    }

    private static bool Matches(byte[] hash, string expected) =>
        string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
}
