using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>
/// Deterministic, seamlessly tiling 16x16 ground tiles built only from palette colors.
/// Procedural on purpose: ground needs many cheap variants; hero art is authored or generated.
/// </summary>
public static class TileGenerator
{
    public const int Size = 16;

    public static IEnumerable<SpriteSheet> Generate(Palette pal)
    {
        uint P(string hex)
        {
            uint c = Palette.ParseHex(hex);
            if (!pal.Contains(c)) throw new InvalidDataException($"tile color {hex} not in palette");
            return c | 0xFF000000;
        }

        // Grass: base with soft clusters, blades with lit tips (light from top-left), sparse flowers.
        var grass = new SpriteSheet { Name = "tile_grass", W = Size, H = Size, Origin = [0, 0], Source = "TileGenerator" };
        uint gDeep = P("142a24"), gDark = P("1e3d2c"), gBase = P("2d5a35"), gMid = P("44803e"), gLit = P("6aa84a"), gTip = P("9fcf62");
        uint fYellow = P("ffe26b"), fWhite = P("f4ecea"), fCore = P("ff8a2a"), fPink = P("e070c0");
        for (int v = 0; v < 4; v++)
        {
            var img = new RgbaImage(Size, Size);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float n = Cluster(x, y, 11 + v * 7);
                    img[x, y] = n > 0.72f ? gMid : n < 0.18f ? gDark : gBase;
                }
            var rng = new Random(1000 + v);
            int blades = 7 + v;
            for (int b = 0; b < blades; b++)
            {
                int x = rng.Next(Size), y = rng.Next(Size), len = 2 + rng.Next(2);
                for (int k = 0; k < len; k++) img[Wrap(x), Wrap(y - k)] = k == len - 1 ? gTip : gLit;
                img[Wrap(x + 1), Wrap(y)] = gDeep; // contact shadow, bottom-right
            }
            if (v == 3)
            {
                Flower(img, 4, 5, fYellow, fCore);
                Flower(img, 11, 11, fWhite, fYellow);
            }
            if (v == 2) Flower(img, 9, 4, fPink, fWhite);
            grass.Frames.Add(new SpriteFrame($"grass_{v}", img, new RgbaImage(Size, Size)));
            grass.Normals.Add(null);
        }
        yield return grass;

        // Ancient stone slabs: mortar lines, bevel highlight top-left, shadow bottom-right, cracks and moss.
        var stone = new SpriteSheet { Name = "tile_stone", W = Size, H = Size, Origin = [0, 0], Source = "TileGenerator" };
        uint sDark = P("2a2340"), sBody = P("4a3f69"), sLit = P("6b5f8e"), sHi = P("968bb4"), moss = P("2d5a35"), mossLit = P("44803e");
        for (int v = 0; v < 2; v++)
        {
            var img = new RgbaImage(Size, Size);
            int split = v == 0 ? 8 : 6; // horizontal slab split
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool mortar = y == 15 || x == 15 || (y == split - 1 && true) || (y < split && x == 7) || (y >= split && x == (v == 0 ? 11 : 3));
                    if (mortar) { img[x, y] = sDark; continue; }
                    bool top = y == 0 || y == split, left = x == 0 || (y < split && x == 8) || (y >= split && x == (v == 0 ? 12 : 4));
                    bool bottom = y == 14 || y == split - 2, right = x == 14 || (y < split && x == 6) || (y >= split && x == (v == 0 ? 10 : 2));
                    float n = Cluster(x, y, 31 + v);
                    img[x, y] = top || left ? sLit : bottom || right ? (n > 0.5f ? sDark : sBody) : n > 0.8f ? sLit : sBody;
                    if (top && left) img[x, y] = sHi;
                }
            // a crack
            int cx = v == 0 ? 3 : 10, cy = v == 0 ? 10 : 2;
            for (int k = 0; k < 4; k++) img[Wrap(cx + k), Wrap(cy + (k % 2))] = sDark;
            // moss in mortar corners
            img[15, 15] = moss; img[14, 15] = mossLit; img[15, 14] = moss;
            if (v == 1) { img[7, 5] = moss; img[7, 4] = mossLit; }
            stone.Frames.Add(new SpriteFrame($"stone_{v}", img, new RgbaImage(Size, Size)));
            stone.Normals.Add(null);
        }
        yield return stone;
    }

    private static int Wrap(int v) => ((v % Size) + Size) % Size;

    private static void Flower(RgbaImage img, int x, int y, uint petal, uint core)
    {
        img[Wrap(x), Wrap(y - 1)] = petal;
        img[Wrap(x - 1), Wrap(y)] = petal;
        img[Wrap(x + 1), Wrap(y)] = petal;
        img[Wrap(x), Wrap(y + 1)] = petal;
        img[Wrap(x), Wrap(y)] = core;
    }

    /// <summary>Tileable clustered value noise in [0,1) on a 4px lattice.</summary>
    private static float Cluster(int x, int y, int seed)
    {
        const int cell = 4, n = Size / cell;
        float fx = (float)x / cell, fy = (float)y / cell;
        int x0 = (int)fx, y0 = (int)fy;
        float tx = fx - x0, ty = fy - y0;
        float L(int i, int j) => Hash(((i % n) + n) % n, ((j % n) + n) % n, seed);
        float a = L(x0, y0) + (L(x0 + 1, y0) - L(x0, y0)) * tx;
        float b = L(x0, y0 + 1) + (L(x0 + 1, y0 + 1) - L(x0, y0 + 1)) * tx;
        return a + (b - a) * ty;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65536f;
        }
    }
}
