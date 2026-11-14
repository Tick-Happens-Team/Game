using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>
/// Fallback normal maps for sprites without an authored one (recipes produce exact normals from their geometry).
/// Height = bevel from the silhouette edge blended with albedo luminance; normals from a Sobel filter.
/// Encoding: RGB = normal * 0.5 + 0.5 with +X right, +Y UP (OpenGL convention), +Z towards the viewer.
/// Alpha copies the sprite alpha.
/// </summary>
public static class NormalMapGenerator
{
    public static RgbaImage Generate(RgbaImage src, int bevel = 3, float strength = 2.2f, float lumaWeight = 0.35f)
    {
        int w = src.W, h = src.H;
        var dist = new float[w * h];
        for (int i = 0; i < dist.Length; i++) dist[i] = RgbaImage.A(src.Px[i]) > 0 ? bevel : 0;
        // Chamfer distance to the nearest transparent pixel (or border), capped at 'bevel'.
        for (int pass = 0; pass < bevel; pass++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (dist[i] == 0) continue;
                    float m = dist[i];
                    m = MathF.Min(m, (x > 0 ? dist[i - 1] : 0) + 1);
                    m = MathF.Min(m, (x < w - 1 ? dist[i + 1] : 0) + 1);
                    m = MathF.Min(m, (y > 0 ? dist[i - w] : 0) + 1);
                    m = MathF.Min(m, (y < h - 1 ? dist[i + w] : 0) + 1);
                    dist[i] = m;
                }

        var height = new float[w * h];
        for (int i = 0; i < height.Length; i++)
        {
            uint c = src.Px[i];
            if (RgbaImage.A(c) == 0) continue;
            float luma = (0.299f * (c & 255) + 0.587f * ((c >> 8) & 255) + 0.114f * ((c >> 16) & 255)) / 255f;
            height[i] = (1 - lumaWeight) * (dist[i] / bevel) + lumaWeight * luma;
        }

        float Hgt(int x, int y) => height[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        var dst = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint c = src[x, y];
                if (RgbaImage.A(c) == 0) continue;
                float gx = Hgt(x + 1, y - 1) + 2 * Hgt(x + 1, y) + Hgt(x + 1, y + 1) - (Hgt(x - 1, y - 1) + 2 * Hgt(x - 1, y) + Hgt(x - 1, y + 1));
                float gy = Hgt(x - 1, y + 1) + 2 * Hgt(x, y + 1) + Hgt(x + 1, y + 1) - (Hgt(x - 1, y - 1) + 2 * Hgt(x, y - 1) + Hgt(x + 1, y - 1));
                // Image y grows downward; flip so +Y is up.
                float nx = -gx * strength, ny = gy * strength, nz = 1;
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                nx /= len; ny /= len; nz /= len;
                dst[x, y] = RgbaImage.Pack((int)MathF.Round((nx * 0.5f + 0.5f) * 255), (int)MathF.Round((ny * 0.5f + 0.5f) * 255),
                    (int)MathF.Round((nz * 0.5f + 0.5f) * 255), RgbaImage.A(c));
            }
        return dst;
    }
}
