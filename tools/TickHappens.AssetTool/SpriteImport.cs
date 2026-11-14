using System.Text.Json;
using System.Text.Json.Serialization;
using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>
/// Sidecar manifest for imported sheets (hand-drawn in an external editor or any other source): art/source/imported/NAME.sprite.json next to NAME.png.
/// Records provenance so every generated asset is reproducible and auditable.
/// </summary>
public sealed class SpriteManifest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("normal")] public string? Normal { get; set; }
    [JsonPropertyName("frame")] public int[] Frame { get; set; } = [0, 0];
    [JsonPropertyName("origin")] public int[]? Origin { get; set; }
    /// <summary>Palette colors (RRGGBB) whose pixels also go to the emission map.</summary>
    [JsonPropertyName("emissive")] public List<string> Emissive { get; set; } = [];
    [JsonPropertyName("anims")] public Dictionary<string, SpriteAnim> Anims { get; set; } = [];
    [JsonPropertyName("provenance")] public Provenance Provenance { get; set; } = new();
}

public sealed class Provenance
{
    [JsonPropertyName("tool")] public string Tool { get; set; } = "manual";
    [JsonPropertyName("assetId")] public string? AssetId { get; set; }
    [JsonPropertyName("prompt")] public string? Prompt { get; set; }
    [JsonPropertyName("references")] public List<string> References { get; set; } = [];
    [JsonPropertyName("palette")] public string? Palette { get; set; }
    [JsonPropertyName("importedScale")] public int ImportedScale { get; set; } = 1;
    [JsonPropertyName("license")] public string License { get; set; } = "CC-BY-4.0";
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

public static class SpriteImport
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Normalizes an external PNG: removes a flat backdrop, detects and removes integer upscaling, binarizes alpha,
    /// snaps every pixel to the project palette and writes art/source/imported/NAME.png + NAME.sprite.json.
    /// </summary>
    public static SpriteManifest Import(string src, string name, int frameW, int frameH, Palette pal, string outDir, Provenance prov, string? normalSrc)
    {
        var img = RgbaImage.Load(src);
        PixelCleanup.RemoveCornerBackground(img);
        int scale = PixelScale.Detect(img);
        if (scale > 1) img = PixelScale.Downsample(img, scale);
        PixelCleanup.SnapToPalette(img, pal);
        PixelCleanup.Despeckle(img);
        PixelCleanup.FillHoles(img);
        if (img.W % frameW != 0 || img.H % frameH != 0)
            throw new InvalidDataException($"{src}: {img.W}x{img.H} (after x{scale} downscale) is not a grid of {frameW}x{frameH} frames");

        Directory.CreateDirectory(outDir);
        img.Save(Path.Combine(outDir, name + ".png"));
        string? normalName = null;
        if (normalSrc != null)
        {
            var n = RgbaImage.Load(normalSrc);
            if (scale > 1 && n.W != img.W) n = PixelScale.Downsample(n, scale);
            normalName = name + "_n.png";
            n.Save(Path.Combine(outDir, normalName));
        }
        prov.ImportedScale = scale;
        prov.Palette = pal.Name;
        var m = new SpriteManifest { Name = name, Image = name + ".png", Normal = normalName, Frame = [frameW, frameH], Provenance = prov };
        int count = img.W / frameW * (img.H / frameH);
        m.Anims["default"] = new SpriteAnim { Fps = 8, Frames = [.. Enumerable.Range(0, count)] };
        File.WriteAllText(Path.Combine(outDir, name + ".sprite.json"), JsonSerializer.Serialize(m, Json));
        return m;
    }

    /// <summary>Loads a sidecar manifest into a sheet (frames in row-major order).</summary>
    public static SpriteSheet Load(string manifestPath)
    {
        var m = JsonSerializer.Deserialize<SpriteManifest>(File.ReadAllText(manifestPath), Json) ?? throw new InvalidDataException(manifestPath);
        string dir = Path.GetDirectoryName(manifestPath)!;
        var img = RgbaImage.Load(Path.Combine(dir, m.Image));
        var normal = m.Normal != null ? RgbaImage.Load(Path.Combine(dir, m.Normal)) : null;
        int fw = m.Frame[0], fh = m.Frame[1];
        var emissive = m.Emissive.Select(Palette.ParseHex).ToHashSet();
        var sheet = new SpriteSheet { Name = m.Name, W = fw, H = fh, Origin = m.Origin ?? [fw / 2, fh - 1], Source = manifestPath };
        int n = 0;
        for (int y = 0; y + fh <= img.H; y += fh)
            for (int x = 0; x + fw <= img.W; x += fw)
            {
                var color = img.Crop(x, y, fw, fh);
                var emit = new RgbaImage(fw, fh);
                for (int i = 0; i < color.Px.Length; i++)
                    if (RgbaImage.A(color.Px[i]) != 0 && emissive.Contains(RgbaImage.Rgb(color.Px[i]))) emit.Px[i] = color.Px[i];
                sheet.Frames.Add(new SpriteFrame($"f{n++}", color, emit));
                sheet.Normals.Add(normal?.Crop(x, y, fw, fh));
            }
        foreach (var (k, v) in m.Anims) sheet.Anims[k] = v;
        return sheet;
    }
}
