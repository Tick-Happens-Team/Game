using System.Text.Json;
using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>Shelf-packs sprite frames into albedo / emission / normal atlases sharing one layout.</summary>
public static class AtlasBuilder
{
    public const int Padding = 1;

    public static AtlasData Build(IReadOnlyList<SpriteSheet> sheets, string outBase)
    {
        var items = sheets.SelectMany(s => s.Frames.Select((f, i) => (Sheet: s, Index: i)))
            .OrderByDescending(t => t.Sheet.H).ThenByDescending(t => t.Sheet.W).ThenBy(t => t.Sheet.Name).ThenBy(t => t.Index).ToList();

        int width = 256;
        while (true)
        {
            if (TryPack(items, width, out var placements, out int height))
            {
                return Write(sheets, placements, width, NextPow2(height), outBase);
            }
            width *= 2;
            if (width > 8192) throw new InvalidOperationException("atlas too large");
        }
    }

    private static bool TryPack(List<(SpriteSheet Sheet, int Index)> items, int width, out Dictionary<(string, int), (int X, int Y)> placements, out int height)
    {
        placements = [];
        int x = 0, y = 0, shelf = 0;
        foreach (var (s, i) in items)
        {
            int w = s.W + Padding, h = s.H + Padding;
            if (w > width) { height = 0; return false; }
            if (x + w > width) { x = 0; y += shelf; shelf = 0; }
            placements[(s.Name, i)] = (x, y);
            x += w;
            shelf = Math.Max(shelf, h);
        }
        height = y + shelf;
        return height <= width; // keep the atlas roughly square
    }

    private static int NextPow2(int v) { int p = 1; while (p < v) p <<= 1; return p; }

    private static AtlasData Write(IReadOnlyList<SpriteSheet> sheets, Dictionary<(string, int), (int X, int Y)> placements, int w, int h, string outBase)
    {
        var albedo = new RgbaImage(w, h);
        var emission = new RgbaImage(w, h);
        var normals = new RgbaImage(w, h);
        var data = new AtlasData { Size = [w, h] };
        foreach (var s in sheets.OrderBy(s => s.Name))
        {
            var entry = new AtlasSprite { W = s.W, H = s.H, Origin = s.Origin };
            for (int i = 0; i < s.Frames.Count; i++)
            {
                var (x, y) = placements[(s.Name, i)];
                var f = s.Frames[i];
                albedo.Blit(f.Color, x, y);
                emission.Blit(f.Emission, x, y);
                normals.Blit((i < s.Normals.Count ? s.Normals[i] : null) ?? NormalMapGenerator.Generate(f.Color, bevel: s.Name.StartsWith("tile_") ? 1 : 3), x, y);
                entry.Frames.Add([x, y]);
                entry.FrameNames.Add(f.Name);
            }
            foreach (var (k, v) in s.Anims) entry.Anims[k] = v;
            data.Sprites[s.Name] = entry;
        }
        albedo.Save(outBase + ".png");
        emission.Save(outBase + "_e.png");
        normals.Save(outBase + "_n.png");
        File.WriteAllText(outBase + ".json", JsonSerializer.Serialize(data, AtlasData.Json));
        return data;
    }
}
