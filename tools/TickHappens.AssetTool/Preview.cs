using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>Writes enlarged review sheets (checkerboard background, all frames side by side) for visual QA.</summary>
public static class Preview
{
    public const int Zoom = 8;

    public static void Write(IEnumerable<SpriteSheet> sheets, string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var s in sheets)
        {
            int n = s.Frames.Count, gap = 2;
            var img = new RgbaImage((s.W + gap) * n * Zoom, s.H * Zoom * 2 + gap * Zoom);
            for (int i = 0; i < n; i++)
            {
                // top row: albedo on checker; bottom row: albedo on mid-grass (in-context readability)
                Draw(img, s.Frames[i].Color, i * (s.W + gap) * Zoom, 0, checker: true);
                Draw(img, s.Frames[i].Color, i * (s.W + gap) * Zoom, (s.H + gap) * Zoom, checker: false);
            }
            img.Save(Path.Combine(dir, s.Name + ".png"));
        }
    }

    private static void Draw(RgbaImage dst, RgbaImage src, int ox, int oy, bool checker)
    {
        uint grass = RgbaImage.Pack(0x2d, 0x5a, 0x35);
        for (int y = 0; y < src.H * Zoom; y++)
            for (int x = 0; x < src.W * Zoom; x++)
            {
                uint c = src[x / Zoom, y / Zoom];
                uint bg = checker ? (((x / Zoom) + (y / Zoom)) % 2 == 0 ? RgbaImage.Pack(60, 60, 70) : RgbaImage.Pack(80, 80, 92)) : grass;
                dst[ox + x, oy + y] = RgbaImage.A(c) == 0 ? bg : c;
            }
    }

    /// <summary>One row per sheet (e.g. per seed), all frames, on night-grass background, enlarged.</summary>
    public static void ContactSheet(IReadOnlyList<SpriteSheet> rows, string path, int zoom)
    {
        int cols = rows.Max(r => r.Frames.Count), w = rows.Max(r => r.W) + 4, h = rows.Max(r => r.H) + 4;
        var img = new RgbaImage(cols * w * zoom, rows.Count * h * zoom);
        uint bg = RgbaImage.Pack(0x1e, 0x3d, 0x2c), bg2 = RgbaImage.Pack(0x2d, 0x5a, 0x35);
        for (int i = 0; i < img.Px.Length; i++) img.Px[i] = ((i % img.W) / (zoom * 4) + (i / img.W) / (zoom * 4)) % 2 == 0 ? bg : bg2;
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Frames.Count; c++)
            {
                var f = rows[r].Frames[c].Color;
                for (int y = 0; y < f.H * zoom; y++)
                    for (int x = 0; x < f.W * zoom; x++)
                    {
                        uint px = f[x / zoom, y / zoom];
                        if (RgbaImage.A(px) != 0) img[(c * w + 2) * zoom + x, (r * h + 2) * zoom + y] = px;
                    }
            }
        img.Save(path);
    }
}
