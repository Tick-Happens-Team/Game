using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickHappens.AssetTool.ProcGen;

/// <summary>
/// A procedural sprite recipe (art/recipes/*.json). The sprite is drawn natively on the pixel grid
/// from bones, shape primitives and palette ramps — never downscaled from a larger image.
/// Same recipe + same seed => identical frames.
/// </summary>
public sealed class Recipe
{
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    [JsonPropertyName("size")] public int[] Size { get; set; } = [16, 16];
    [JsonPropertyName("origin")] public int[]? Origin { get; set; }
    [JsonPropertyName("seed")] public int Seed { get; set; } = 1;
    /// <summary>Light direction in image space (x right, y down, z towards viewer). Default: top-left.</summary>
    [JsonPropertyName("light")] public float[] Light { get; set; } = [-0.55f, -0.7f, 0.6f];
    [JsonPropertyName("outline")] public OutlineSpec Outline { get; set; } = new();
    /// <summary>
    /// Weight of the whole-silhouette volume in each pixel's normal (0 = every part lit on its own,
    /// 1 = the sprite lit as one lump). Blending keeps per-part form while the shadow side stays dark.
    /// </summary>
    [JsonPropertyName("unify")] public float Unify { get; set; } = 0.45f;
    [JsonPropertyName("materials")] public Dictionary<string, MaterialSpec> Materials { get; set; } = [];
    [JsonPropertyName("bones")] public Dictionary<string, BoneSpec> Bones { get; set; } = [];
    [JsonPropertyName("parts")] public List<PartSpec> Parts { get; set; } = [];
    [JsonPropertyName("decals")] public List<DecalSpec> Decals { get; set; } = [];
    [JsonPropertyName("anims")] public Dictionary<string, AnimSpec> Anims { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static Recipe Load(string path) =>
        JsonSerializer.Deserialize<Recipe>(File.ReadAllText(path), Json) ?? throw new InvalidDataException(path);
}

public sealed class OutlineSpec
{
    [JsonPropertyName("color")] public string Color { get; set; } = "1a1422";
    /// <summary>Selective outline: lit (top/left) edges use the darkest tone of the touching material instead of ink.</summary>
    [JsonPropertyName("selout")] public bool Selout { get; set; } = true;
}

public sealed class MaterialSpec
{
    /// <summary>Palette colors from shadow to light.</summary>
    [JsonPropertyName("ramp")] public List<string> Ramp { get; set; } = [];
    /// <summary>Alternative ramps (palette-swap variants): seed 1 uses Ramp, seed 2 the first variant, and so on cyclically.</summary>
    [JsonPropertyName("variants")] public List<List<string>>? Variants { get; set; }
    /// <summary>Lighting thresholds over 0..1 (N·L remapped); n thresholds give n+1 tones. Default: 2 thresholds, 3 tones.</summary>
    [JsonPropertyName("bands")] public float[] Bands { get; set; } = [0.38f, 0.72f];
    /// <summary>Index into the ramp of the darkest band (the ramp may hold more tones than bands, e.g. a highlight).</summary>
    [JsonPropertyName("base")] public int Base { get; set; } = 0;
    /// <summary>Specular: pixels with N·L above this use the ramp's last color (0 = off).</summary>
    [JsonPropertyName("spec")] public float Spec { get; set; }
    [JsonPropertyName("emissive")] public bool Emissive { get; set; }
    /// <summary>Half-width of an ordered (Bayer 4x4) dither zone around each band threshold; 0 = hard bands (default, best for small sprites).</summary>
    [JsonPropertyName("dither")] public float Dither { get; set; }
}

public sealed class BoneSpec
{
    [JsonPropertyName("parent")] public string? Parent { get; set; }
    [JsonPropertyName("at")] public float[] At { get; set; } = [0, 0];
}

public sealed class PartSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bone")] public string Bone { get; set; } = "root";
    /// <summary>ellipse | rect | poly | line</summary>
    [JsonPropertyName("shape")] public string Shape { get; set; } = "ellipse";
    /// <summary>ellipse: center; rect: top-left; relative to the bone.</summary>
    [JsonPropertyName("c")] public float[] C { get; set; } = [0, 0];
    /// <summary>ellipse: radii; rect: width, height; line: thickness in [0].</summary>
    [JsonPropertyName("r")] public float[] R { get; set; } = [1, 1];
    /// <summary>poly / line points relative to the bone.</summary>
    [JsonPropertyName("pts")] public List<float[]>? Pts { get; set; }
    /// <summary>Rounded rect corner radius in pixels.</summary>
    [JsonPropertyName("round")] public float Round { get; set; }
    [JsonPropertyName("mat")] public string Mat { get; set; } = "";
    [JsonPropertyName("z")] public float Z { get; set; }
    /// <summary>dome (ellipsoid) | cyl (vertical cylinder) | bevel (from silhouette distance) | flat</summary>
    [JsonPropertyName("profile")] public string Profile { get; set; } = "dome";
    /// <summary>Mirror the part across the bone's x axis (adds a second copy).</summary>
    [JsonPropertyName("mirror")] public bool Mirror { get; set; }
    /// <summary>Darken (tone -1) pixels of other parts directly below/right of this one (contact shadow).</summary>
    [JsonPropertyName("shadow")] public bool CastShadow { get; set; } = true;

    public PartSpec With(PartOverride o)
    {
        var p = (PartSpec)MemberwiseClone();
        if (o.C != null) p.C = o.C;
        if (o.R != null) p.R = o.R;
        if (o.Pts != null) p.Pts = o.Pts;
        if (o.Z != null) p.Z = o.Z.Value;
        if (o.Mat != null) p.Mat = o.Mat;
        return p;
    }
}

public sealed class DecalSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bone")] public string Bone { get; set; } = "root";
    /// <summary>Top-left of the stamp relative to the bone.</summary>
    [JsonPropertyName("at")] public int[] At { get; set; } = [0, 0];
    /// <summary>Pixel rows; '.' is "leave as is".</summary>
    [JsonPropertyName("rows")] public List<string> Rows { get; set; } = [];
    [JsonPropertyName("keys")] public Dictionary<string, string> Keys { get; set; } = [];
    /// <summary>Keys whose pixels also go to the emission map.</summary>
    [JsonPropertyName("emissive")] public List<string> Emissive { get; set; } = [];
    [JsonPropertyName("mirror")] public bool Mirror { get; set; }
}

public sealed class AnimSpec
{
    [JsonPropertyName("fps")] public float Fps { get; set; } = 8;
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
    [JsonPropertyName("frames")] public List<FrameSpec> Frames { get; set; } = [];
}

public sealed class FrameSpec
{
    /// <summary>Per-bone offset [dx, dy] or [dx, dy, sx, sy] (scale applies to that bone's own parts).</summary>
    [JsonPropertyName("bones")] public Dictionary<string, float[]> Bones { get; set; } = [];
    [JsonPropertyName("parts")] public Dictionary<string, PartOverride> Parts { get; set; } = [];
    [JsonPropertyName("hide")] public List<string> Hide { get; set; } = [];
    /// <summary>Decal names to hide or alternate decals to show (e.g. blink).</summary>
    [JsonPropertyName("hideDecals")] public List<string> HideDecals { get; set; } = [];
}

public sealed class PartOverride
{
    [JsonPropertyName("c")] public float[]? C { get; set; }
    [JsonPropertyName("r")] public float[]? R { get; set; }
    [JsonPropertyName("pts")] public List<float[]>? Pts { get; set; }
    [JsonPropertyName("z")] public float? Z { get; set; }
    [JsonPropertyName("mat")] public string? Mat { get; set; }
}
