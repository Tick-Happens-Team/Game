namespace TickHappens.Engine.Vfx;

/// <summary>Current sprite frame in world art pixels, backed by its atlas image.</summary>
public readonly record struct ActorSilhouette(
    int X, int Y, int Width, int Height, ReadOnlyMemory<uint> Pixels,
    int Stride, int Offset, bool FlipX = false)
{
    public bool IsOpaque(int x, int y, uint minAlpha = 1)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        int column = FlipX ? Width - 1 - x : x;
        return (Pixels.Span[Offset + y * Stride + column] >> 24) >= minAlpha;
    }
}
