using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickHappens.Engine.Assets;

/// <summary>
/// Atlas metadata written by the asset tool. One layout is shared by three images:
/// albedo (name.png), emission (name_e.png) and normals (name_n.png).
/// </summary>
public sealed class AtlasData
{
    [JsonPropertyName("size")] public int[] Size { get; set; } = [0, 0];
    [JsonPropertyName("sprites")] public Dictionary<string, AtlasSprite> Sprites { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static AtlasData Load(string path) =>
        JsonSerializer.Deserialize<AtlasData>(File.ReadAllText(path), Json) ?? throw new InvalidDataException(path);
}

public sealed class AtlasSprite
{
    /// <summary>Frame size in art pixels.</summary>
    [JsonPropertyName("w")] public int W { get; set; }
    [JsonPropertyName("h")] public int H { get; set; }
    /// <summary>Pivot inside the frame (feet for actors).</summary>
    [JsonPropertyName("origin")] public int[] Origin { get; set; } = [0, 0];
    /// <summary>Top-left of each frame inside the atlas.</summary>
    [JsonPropertyName("frames")] public List<int[]> Frames { get; set; } = [];
    [JsonPropertyName("frameNames")] public List<string> FrameNames { get; set; } = [];
    [JsonPropertyName("anims")] public Dictionary<string, SpriteAnim> Anims { get; set; } = [];
}

public sealed class SpriteAnim
{
    [JsonPropertyName("fps")] public float Fps { get; set; } = 8;
    [JsonPropertyName("frames")] public List<int> Frames { get; set; } = [];
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
}
