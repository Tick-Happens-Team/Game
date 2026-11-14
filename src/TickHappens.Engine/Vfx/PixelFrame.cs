// Port of the Sprite Fusion pixel VFX frame model. MIT License, Copyright (c) 2026 Hugo Duprez.
// See LICENSE-SpriteFusion-VFX.txt in this directory.
namespace TickHappens.Engine.Vfx;

/// <summary>
/// A pair of pixel buffers that VFX rasterize into, in art pixels.
/// Color holds the visible pixels (alpha-blended over the scene),
/// Light holds additive emission (fed into bloom and the light buffer).
/// Pixels are packed as 0xAABBGGRR, which is RGBA8 byte order on little-endian CPUs.
/// (Ox, Oy) is the art-pixel position of the buffer's top-left corner, so a camera-aligned
/// frame lets effects simulate in world space.
/// </summary>
public sealed class PixelFrame
{
    public int W { get; private set; }
    public int H { get; private set; }
    public int Ox;
    public int Oy;
    public uint[] Color { get; private set; }
    public uint[] Light { get; private set; }

    public PixelFrame(int w, int h)
    {
        W = w;
        H = h;
        Color = new uint[w * h];
        Light = new uint[w * h];
    }

    public void Resize(int w, int h)
    {
        if (w == W && h == H) return;
        W = w;
        H = h;
        Color = new uint[w * h];
        Light = new uint[w * h];
    }

    public void Clear()
    {
        Array.Clear(Color);
        Array.Clear(Light);
    }
}

public enum VfxLayer
{
    /// <summary>Draw both layers.</summary>
    All,
    /// <summary>Behind actors (ground scorch, smoke).</summary>
    Back,
    /// <summary>In front of actors.</summary>
    Front,
}

/// <summary>A procedural pixel effect simulated on the CPU.</summary>
public interface IPixelEffect
{
    void Update(float dt);
    void Draw(PixelFrame frame, VfxLayer layer);
    bool Done { get; }
}
