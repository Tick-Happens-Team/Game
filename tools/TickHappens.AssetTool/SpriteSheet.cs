using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>A sprite with named frames, before atlas packing.</summary>
public sealed class SpriteSheet
{
    public required string Name { get; init; }
    public required int W { get; init; }
    public required int H { get; init; }
    public int[] Origin { get; init; } = [0, 0];
    public List<SpriteFrame> Frames { get; } = [];
    public Dictionary<string, SpriteAnim> Anims { get; } = [];
    /// <summary>Where it came from (for lint messages).</summary>
    public required string Source { get; init; }
    /// <summary>Optional authored normal maps (e.g. from recipe geometry), parallel to Frames.</summary>
    public List<RgbaImage?> Normals { get; } = [];

    public int FrameIndex(string name)
    {
        int i = Frames.FindIndex(f => f.Name == name);
        if (i < 0) throw new InvalidDataException($"{Source}: unknown frame '{name}'");
        return i;
    }
}

public sealed record SpriteFrame(string Name, RgbaImage Color, RgbaImage Emission);
