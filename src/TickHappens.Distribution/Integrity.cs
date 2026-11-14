using System.Security.Cryptography;
using System.Text;

namespace TickHappens.Distribution;

public enum IssueKind { Missing, SizeMismatch, HashMismatch, Extra }

public sealed record IntegrityIssue(IssueKind Kind, string Path)
{
    public override string ToString() => $"{Kind}: {Path}";
}

public static class Integrity
{
    /// <summary>
    /// Verifies an installed directory against its manifest. Size is checked first (cheap), hashes only when sizes match.
    /// Extra files are reported (user-added files inside the install dir are not allowed; saves/configs live elsewhere).
    /// </summary>
    public static List<IntegrityIssue> Verify(string dir, PackageManifest manifest, bool reportExtra = true)
    {
        manifest.ValidatePaths();
        var issues = new List<IntegrityIssue>();
        string root = Path.GetFullPath(dir);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in manifest.Files)
        {
            expected.Add(f.Path);
            string full = Path.Combine(root, f.Path);
            if (!File.Exists(full)) { issues.Add(new(IssueKind.Missing, f.Path)); continue; }
            if (new FileInfo(full).Length != f.Size) { issues.Add(new(IssueKind.SizeMismatch, f.Path)); continue; }
            if (!string.Equals(PackageManifest.Hash(full), f.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(IssueKind.HashMismatch, f.Path));
        }
        if (reportExtra && Directory.Exists(root))
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel != InstallLayout.ManifestFileName && !expected.Contains(rel)) issues.Add(new(IssueKind.Extra, rel));
            }
        issues.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return issues;
    }
}

/// <summary>
/// Detached ECDSA P-256 / SHA-256 signatures over manifest bytes (built into .NET, no extra dependency).
/// The private key lives only in CI secrets; the launcher embeds the public key(s).
/// Signature files hold base64 of the IEEE P1363 (r||s) signature.
/// </summary>
public static class ManifestSigner
{
    public static (string PrivatePem, string PublicPem) CreateKeyPair()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (k.ExportPkcs8PrivateKeyPem(), k.ExportSubjectPublicKeyInfoPem());
    }

    public static string Sign(byte[] data, string privatePem)
    {
        using var k = ECDsa.Create();
        k.ImportFromPem(privatePem);
        return Convert.ToBase64String(k.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>True if any of the trusted public keys verifies the signature (supports key rotation).</summary>
    public static bool Verify(byte[] data, string signatureBase64, IEnumerable<string> trustedPublicPems)
    {
        byte[] sig;
        try { sig = Convert.FromBase64String(signatureBase64.Trim()); }
        catch (FormatException) { return false; }
        foreach (var pem in trustedPublicPems)
        {
            using var k = ECDsa.Create();
            k.ImportFromPem(pem);
            if (k.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return true;
        }
        return false;
    }

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}

/// <summary>Launcher-side compatibility decisions for a channel manifest.</summary>
public static class Compatibility
{
    /// <summary>Checks that the game build accepts the pinned assets pack format and the launcher is new enough.</summary>
    public static List<string> Check(ChannelManifest channel, PackageManifest game, PackageManifest assets, SemVer launcherVersion)
    {
        var errors = new List<string>();
        if (launcherVersion < SemVer.Parse(channel.MinLauncher))
            errors.Add($"launcher {launcherVersion} is older than required {channel.MinLauncher}; self-update first");
        if (game.Kind != "game") errors.Add($"expected a game package, got '{game.Kind}'");
        if (assets.Kind != "assets") errors.Add($"expected an assets package, got '{assets.Kind}'");
        if (game.Requires.TryGetValue("assetsFormat", out var range))
        {
            var fmt = new SemVer(assets.Format, 0, 0);
            if (!new VersionRange(range).Contains(fmt)) errors.Add($"game {game.Version} needs assets format {range}, pack {assets.Version} has {assets.Format}");
        }
        else errors.Add($"game {game.Version} does not declare requires.assetsFormat");
        if (assets.Version != channel.Assets.Version) errors.Add($"assets pack {assets.Version} is not the one pinned by the channel ({channel.Assets.Version})");
        return errors;
    }

    /// <summary>Rejects a channel manifest older than the last one accepted (rollback/replay protection).</summary>
    public static bool IsNewer(ChannelManifest candidate, long lastAcceptedSequence) => candidate.Sequence > lastAcceptedSequence;
}
