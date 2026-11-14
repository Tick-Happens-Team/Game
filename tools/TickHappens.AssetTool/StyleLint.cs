using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

public enum Severity { Warning, Error }

public sealed record LintIssue(Severity Severity, string Code, string Where, string Message)
{
    public override string ToString() => $"{(Severity == Severity.Error ? "error" : "warn ")} {Code} {Where}: {Message}";
}

/// <summary>
/// Automated checks for docs/ART_STANDARD.md. Catches the usual signs of "AI image shrunk to pixels":
/// off-palette colors, soft alpha, fat (upscaled) pixels, noisy single-pixel speckle, missing dark outline.
/// Sprites named vfx_* are exempt from palette and alpha rules (emissive blends are allowed there).
/// </summary>
public static class StyleLint
{
    public const int MaxColorsPerSprite = 24;
    public static readonly int[] StandardSizes = [8, 12, 16, 24, 32, 48, 64, 96, 128];

    public static List<LintIssue> Check(SpriteSheet s, Palette pal)
    {
        var issues = new List<LintIssue>();
        bool vfx = s.Name.StartsWith("vfx_"), tile = s.Name.StartsWith("tile_");
        if (!vfx && (!StandardSizes.Contains(s.W) || !StandardSizes.Contains(s.H)))
            issues.Add(new(Severity.Warning, "TH005", s.Name, $"frame size {s.W}x{s.H} is not a standard size ({string.Join(",", StandardSizes)})"));

        var colors = new HashSet<uint>();
        foreach (var f in s.Frames)
        {
            string where = $"{s.Name}/{f.Name}";
            int offPalette = 0, softAlpha = 0, opaque = 0;
            foreach (uint c in f.Color.Px)
            {
                byte a = RgbaImage.A(c);
                if (a == 0) continue;
                opaque++;
                colors.Add(RgbaImage.Rgb(c));
                if (a != 255) softAlpha++;
                if (!pal.Contains(c)) offPalette++;
            }
            if (opaque == 0) { issues.Add(new(Severity.Error, "TH000", where, "frame is empty")); continue; }
            if (!vfx && offPalette > 0) issues.Add(new(Severity.Error, "TH001", where, $"{offPalette} pixels use colors outside palette {pal.Name}"));
            if (!vfx && softAlpha > 0) issues.Add(new(Severity.Error, "TH002", where, $"{softAlpha} semi-transparent pixels (alpha must be 0 or 255)"));

            int scale = PixelScale.Detect(f.Color);
            if (scale > 1) issues.Add(new(Severity.Error, "TH003", where, $"art is upscaled x{scale}; import it at native resolution"));

            float speckle = Speckle(f.Color);
            if (!vfx && speckle > 0.30f) issues.Add(new(Severity.Warning, "TH004", where, $"{speckle:P0} isolated pixels - noisy, reads as filtered/AI downscale"));

            if (!vfx && !tile)
            {
                float outline = DarkEdgeRatio(f.Color);
                if (outline < 0.6f) issues.Add(new(Severity.Warning, "TH006", where, $"only {outline:P0} of the silhouette edge is dark; actors need a readable outline"));
            }
        }
        if (!vfx && colors.Count > MaxColorsPerSprite)
            issues.Add(new(Severity.Warning, "TH007", s.Name, $"{colors.Count} colors (limit {MaxColorsPerSprite} per sprite)"));
        if (!vfx && !tile) CheckArtifacts(s, issues);
        return issues;
    }

    /// <summary>Artifact scan for actor/prop frames: pin holes, detached specks, clipping at the frame edge, silhouette jumps in animations.</summary>
    private static void CheckArtifacts(SpriteSheet s, List<LintIssue> issues)
    {
        var area = new int[s.Frames.Count];
        for (int fi = 0; fi < s.Frames.Count; fi++)
        {
            var f = s.Frames[fi];
            var img = f.Color;
            string where = $"{s.Name}/{f.Name}";
            int holes = 0;
            for (int y = 1; y < img.H - 1; y++)
                for (int x = 1; x < img.W - 1; x++)
                    if (RgbaImage.A(img[x, y]) == 0 && RgbaImage.A(img[x - 1, y]) != 0 && RgbaImage.A(img[x + 1, y]) != 0
                        && RgbaImage.A(img[x, y - 1]) != 0 && RgbaImage.A(img[x, y + 1]) != 0) holes++;
            if (holes > 0) issues.Add(new(Severity.Error, "TH008", where, $"{holes} pin holes inside the silhouette"));

            var comps = PixelCleanup.Components(img);
            if (comps.Count > 1)
            {
                int biggest = comps.Max(c => c.Count);
                int specks = comps.Count(c => c.Count <= 3 && c.Count < biggest);
                if (specks > 0) issues.Add(new(Severity.Error, "TH009", where, $"{specks} detached specks (<= 3 px)"));
            }

            // Clipping: a body (non-outline) pixel on the top/side edge means the silhouette continues past the frame.
            int clipped = 0;
            bool Body(int x, int y) => RgbaImage.A(img[x, y]) != 0 && Palette.OkLab(img[x, y]).L >= 0.3f;
            for (int x = 0; x < img.W; x++) if (Body(x, 0)) clipped++;
            for (int y = 0; y < img.H; y++)
            {
                if (Body(0, y)) clipped++;
                if (Body(img.W - 1, y)) clipped++;
            }
            if (clipped > 0) issues.Add(new(Severity.Warning, "TH010", where, $"{clipped} body pixels on the top/side edge (silhouette clipped by the frame)"));
            area[fi] = img.Px.Count(c => RgbaImage.A(c) != 0);
        }
        foreach (var (name, anim) in s.Anims)
            for (int k = 0; k + 1 < anim.Frames.Count; k++)
            {
                int a = area[anim.Frames[k]], b = area[anim.Frames[k + 1]];
                if (a > 0 && b > 0 && MathF.Abs(a - b) / MathF.Max(a, b) > 0.25f)
                    issues.Add(new(Severity.Warning, "TH011", $"{s.Name}/{name}", $"silhouette area jumps {a}->{b} px between frames {k} and {k + 1}"));
            }
    }

    /// <summary>Share of opaque pixels whose color matches none of their 8 neighbours.</summary>
    public static float Speckle(RgbaImage img)
    {
        int opaque = 0, lonely = 0;
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
            {
                uint c = img[x, y];
                if (RgbaImage.A(c) == 0) continue;
                opaque++;
                bool match = false;
                for (int dy = -1; dy <= 1 && !match; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= img.W || ny >= img.H) continue;
                        if (img[nx, ny] == c) { match = true; break; }
                    }
                if (!match) lonely++;
            }
        return opaque == 0 ? 0 : (float)lonely / opaque;
    }

    /// <summary>Share of silhouette edge pixels that are dark (OKLab L &lt; 0.42).</summary>
    public static float DarkEdgeRatio(RgbaImage img)
    {
        int edge = 0, dark = 0;
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
            {
                uint c = img[x, y];
                if (RgbaImage.A(c) == 0) continue;
                bool isEdge = x == 0 || y == 0 || x == img.W - 1 || y == img.H - 1
                    || RgbaImage.A(img[x - 1, y]) == 0 || RgbaImage.A(img[x + 1, y]) == 0
                    || RgbaImage.A(img[x, y - 1]) == 0 || RgbaImage.A(img[x, y + 1]) == 0;
                if (!isEdge) continue;
                edge++;
                if (Palette.OkLab(c).L < 0.42f) dark++;
            }
        return edge == 0 ? 1 : (float)dark / edge;
    }
}

/// <summary>Detects integer upscaling ("fat pixels") and restores native resolution.</summary>
public static class PixelScale
{
    /// <summary>Largest k in 2..8 where ≥92% of k×k grid blocks (best offset) are a single color; 1 otherwise.</summary>
    public static int Detect(RgbaImage img, float threshold = 0.92f)
    {
        for (int k = 8; k >= 2; k--)
        {
            if (img.W < k * 4 || img.H < k * 4) continue;
            for (int oy = 0; oy < k; oy++)
                for (int ox = 0; ox < k; ox++)
                    if (UniformShare(img, k, ox, oy) >= threshold) return k;
        }
        return 1;
    }

    private static float UniformShare(RgbaImage img, int k, int ox, int oy)
    {
        int blocks = 0, uniform = 0;
        for (int by = oy; by + k <= img.H; by += k)
            for (int bx = ox; bx + k <= img.W; bx += k)
            {
                uint c0 = img[bx, by];
                bool allClear = true, same = true;
                for (int y = by; y < by + k && same; y++)
                    for (int x = bx; x < bx + k; x++)
                    {
                        uint c = img[x, y];
                        if (RgbaImage.A(c) != 0) allClear = false;
                        if (c != c0) { same = false; break; }
                    }
                if (allClear) continue;
                blocks++;
                if (same) uniform++;
            }
        return blocks < 8 ? 0 : (float)uniform / blocks;
    }

    /// <summary>Downsamples by k taking each block's most frequent color (mode), alpha thresholded at 50%.</summary>
    public static RgbaImage Downsample(RgbaImage img, int k, int ox = 0, int oy = 0)
    {
        int w = (img.W - ox) / k, h = (img.H - oy) / k;
        var dst = new RgbaImage(w, h);
        var counts = new Dictionary<uint, int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                counts.Clear();
                int opaque = 0;
                for (int j = 0; j < k; j++)
                    for (int i = 0; i < k; i++)
                    {
                        uint c = img[ox + x * k + i, oy + y * k + j];
                        if (RgbaImage.A(c) < 128) continue;
                        opaque++;
                        uint key = RgbaImage.Rgb(c);
                        counts[key] = counts.GetValueOrDefault(key) + 1;
                    }
                if (opaque * 2 < k * k) continue;
                dst[x, y] = counts.MaxBy(p => p.Value).Key | 0xFF000000;
            }
        return dst;
    }
}
