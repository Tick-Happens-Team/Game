using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>
/// Shared artifact fixes for every sprite source (recipes, imported art): stray specks, pin holes,
/// lonely shading pixels, soft alpha and off-palette colors. All passes are deterministic.
/// </summary>
public static class PixelCleanup
{
    /// <summary>Removes detached 8-connected components of at most maxSize pixels (unless protected or touching the bottom row).</summary>
    public static int Despeckle(RgbaImage img, int maxSize = 3, bool[]? protectedPx = null)
    {
        var comps = Components(img);
        if (comps.Count < 2) return 0;
        int biggest = comps.Max(c => c.Count), removed = 0;
        foreach (var c in comps)
        {
            if (c.Count > maxSize || c.Count == biggest) continue;
            if (c.Any(i => i / img.W == img.H - 1 || (protectedPx != null && protectedPx[i]))) continue;
            foreach (int i in c) img.Px[i] = 0;
            removed += c.Count;
        }
        return removed;
    }

    /// <summary>Fills transparent pixels whose 4 neighbours are all opaque with the most common neighbour color.</summary>
    public static int FillHoles(RgbaImage img)
    {
        int filled = 0;
        var src = (uint[])img.Px.Clone();
        for (int y = 1; y < img.H - 1; y++)
            for (int x = 1; x < img.W - 1; x++)
            {
                int i = y * img.W + x;
                if (RgbaImage.A(src[i]) != 0) continue;
                uint[] ns = [src[i - 1], src[i + 1], src[i - img.W], src[i + img.W]];
                if (ns.Any(c => RgbaImage.A(c) == 0)) continue;
                img.Px[i] = ns.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                filled++;
            }
        return filled;
    }

    /// <summary>
    /// A shading pixel that matches none of its 8 neighbours while all 4 direct neighbours agree on one color
    /// is noise (a pillow-shading crumb): replace it with that color. Protected pixels (eyes, decals) stay.
    /// </summary>
    public static int RemoveOrphanTones(RgbaImage img, bool[]? protectedPx = null)
    {
        int fixedCount = 0;
        var src = (uint[])img.Px.Clone();
        for (int y = 1; y < img.H - 1; y++)
            for (int x = 1; x < img.W - 1; x++)
            {
                int i = y * img.W + x;
                uint c = src[i];
                if (RgbaImage.A(c) == 0 || (protectedPx != null && protectedPx[i])) continue;
                uint l = src[i - 1], r = src[i + 1], u = src[i - img.W], d = src[i + img.W];
                if (l != r || l != u || l != d || RgbaImage.A(l) == 0 || l == c) continue;
                bool lonely = true;
                foreach (int k in new[] { i - img.W - 1, i - img.W + 1, i + img.W - 1, i + img.W + 1 })
                    if (src[k] == c) { lonely = false; break; }
                if (!lonely) continue;
                img.Px[i] = l;
                fixedCount++;
            }
        return fixedCount;
    }

    /// <summary>
    /// Removes 1px spurs and comb teeth: opaque pixels with at most one opaque 4-neighbour (repeated until stable).
    /// Protected pixels (decals) stay. Keeps minimum feature width readable at 16-32 px.
    /// </summary>
    public static int PruneSpurs(RgbaImage img, bool[]? protectedPx = null)
    {
        int total = 0;
        for (int iter = 0; iter < 4; iter++)
        {
            var remove = new List<int>();
            for (int y = 0; y < img.H; y++)
                for (int x = 0; x < img.W; x++)
                {
                    int i = y * img.W + x;
                    if (RgbaImage.A(img.Px[i]) == 0 || (protectedPx != null && protectedPx[i])) continue;
                    int nb = 0;
                    if (x > 0 && RgbaImage.A(img.Px[i - 1]) != 0) nb++;
                    if (x < img.W - 1 && RgbaImage.A(img.Px[i + 1]) != 0) nb++;
                    if (y > 0 && RgbaImage.A(img.Px[i - img.W]) != 0) nb++;
                    if (y < img.H - 1 && RgbaImage.A(img.Px[i + img.W]) != 0) nb++;
                    if (nb <= 1) remove.Add(i);
                }
            if (remove.Count == 0) break;
            foreach (int i in remove) img.Px[i] = 0;
            total += remove.Count;
        }
        return total;
    }

    /// <summary>Binary alpha (threshold 50%) and nearest palette color for every opaque pixel.</summary>
    public static void SnapToPalette(RgbaImage img, Palette pal)
    {
        for (int i = 0; i < img.Px.Length; i++)
        {
            uint c = img.Px[i];
            img.Px[i] = RgbaImage.A(c) < 128 ? 0 : pal.Nearest(c) | 0xFF000000;
        }
    }

    /// <summary>Removes a flat background color connected to the image corners (for art drawn on a solid backdrop).</summary>
    public static int RemoveCornerBackground(RgbaImage img)
    {
        uint bg = img[0, 0];
        if (RgbaImage.A(bg) == 0) return 0;
        var stack = new Stack<int>();
        var seen = new bool[img.Px.Length];
        foreach (int i in new[] { 0, img.W - 1, (img.H - 1) * img.W, img.Px.Length - 1 })
            if (img.Px[i] == bg) { stack.Push(i); seen[i] = true; }
        int removed = 0;
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            img.Px[i] = 0;
            removed++;
            int x = i % img.W, y = i / img.W;
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= img.W || ny >= img.H) continue;
                int k = ny * img.W + nx;
                if (!seen[k] && img.Px[k] == bg) { seen[k] = true; stack.Push(k); }
            }
        }
        return removed;
    }

    public static List<List<int>> Components(RgbaImage img)
    {
        var seen = new bool[img.Px.Length];
        var comps = new List<List<int>>();
        for (int s = 0; s < img.Px.Length; s++)
        {
            if (seen[s] || RgbaImage.A(img.Px[s]) == 0) continue;
            var comp = new List<int>();
            var stack = new Stack<int>();
            stack.Push(s);
            seen[s] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                comp.Add(i);
                int x = i % img.W, y = i / img.W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= img.W || ny >= img.H) continue;
                        int k = ny * img.W + nx;
                        if (!seen[k] && RgbaImage.A(img.Px[k]) != 0) { seen[k] = true; stack.Push(k); }
                    }
            }
            comps.Add(comp);
        }
        return comps;
    }
}
