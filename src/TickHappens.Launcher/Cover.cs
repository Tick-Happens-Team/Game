using System.Reflection;
using System.Text.Json;
using StbImageSharp;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Vfx;
using static TickHappens.Launcher.Paint;

namespace TickHappens.Launcher;

/// <summary>
/// The play page as a box cover: a plain light ground with faceted shards along the top and bottom edges, the title
/// over a rule, and the playable heroes standing in a row with their idle animation. The atlas and character list are
/// embedded at build time because the launcher runs before the Assets pack is installed. Without them the cover
/// shows the title alone.
/// </summary>
internal sealed class Cover
{
    private const int Heroes = 7, Scale = 3;

    private readonly RgbaImage? _atlas;
    private readonly List<AtlasSprite> _sprites = [];
    private readonly List<Shard> _shards = [];

    private readonly record struct Shard(float X0, float Y0, float X1, float Y1, float X2, float Y2, uint Color, float Alpha);

    public Cover()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var png = assembly.GetManifestResourceStream("atlas.png");
        using var json = assembly.GetManifestResourceStream("atlas.json");
        using var characters = assembly.GetManifestResourceStream("characters.json");
        if (png is null || json is null || characters is null) return;

        var image = ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);
        var pixels = new uint[image.Width * image.Height];
        Buffer.BlockCopy(image.Data, 0, pixels, 0, image.Data.Length);
        _atlas = new RgbaImage(image.Width, image.Height, pixels);
        var atlas = JsonSerializer.Deserialize<AtlasData>(json, AtlasData.Json) ?? new AtlasData();
        // characters.json lists the playable heroes first, in roster order.
        using var roster = JsonDocument.Parse(characters);
        foreach (var hero in roster.RootElement.EnumerateArray())
        {
            if (_sprites.Count == Heroes) break;
            if (hero.TryGetProperty("sprite", out var id) && id.GetString() is { } name && atlas.Sprites.TryGetValue(name, out var sprite))
                _sprites.Add(sprite);
        }
    }

    /// <summary>Builds the shard field once for the page area; positions come from a fixed hash, not a clock.</summary>
    private void BuildShards(int w, int h)
    {
        uint[] tones = [Rgb(0xB9, 0xDC, 0xF2), Rgb(0xCF, 0xE6, 0xF7), Rgb(0x9F, 0xCB, 0xEA), Rgb(0xE4, 0xF1, 0xFB), Rgb(0xC6, 0xD2, 0xF0)];
        for (int band = 0; band < 2; band++)
        {
            float edge = band == 0 ? 0 : h, depth = band == 0 ? 74 : 44;
            for (uint i = 0; i < 26; i++)
            {
                uint a = Hash(i * 7 + (uint)band * 1000 + 1), b = Hash(i * 7 + (uint)band * 1000 + 2), c = Hash(i * 7 + (uint)band * 1000 + 3);
                float cx = (a % 1000) / 1000f * (w + 80) - 40;
                float reach = depth * (0.45f + (b % 1000) / 1000f * 0.75f);
                float spread = 24 + (c % 60);
                float tip = band == 0 ? edge + reach : edge - reach;
                float baseY = band == 0 ? edge - 4 : edge + 4;
                float lean = ((c >> 10) % 80) - 40f;
                _shards.Add(new(cx - spread, baseY, cx + spread * 0.6f, baseY, cx + lean, tip,
                    tones[(a >> 12) % (uint)tones.Length], 0.35f + ((b >> 12) % 100) / 100f * 0.45f));
            }
        }
    }

    public void Draw(PixelCanvas c, int x0, int y0, int w, int h, float time, string tagline)
    {
        Fill(c, x0, y0, w, h, Paper);
        if (_shards.Count == 0) BuildShards(w, h);
        foreach (var s in _shards) Triangle(c, x0, y0, w, h, s);
        DrawSparkles(c, x0, y0, w, h, time);

        // Title over a rule, then the tagline.
        int cx = x0 + w / 2, titleY = y0 + 92;
        Centered(c, "TICK HAPPENS", cx, titleY, Ink, 5);
        int ruleW = Measure("TICK HAPPENS", 5) + 16;
        Fill(c, cx - ruleW / 2, titleY + 42, ruleW, 2, Ink);
        Fill(c, cx - ruleW / 2, titleY + 46, ruleW, 1, Ink);
        Centered(c, tagline, cx, titleY + 54, Muted);

        if (_atlas is null || _sprites.Count == 0) return;
        int spacing = 64, feet = y0 + 254;
        int first = cx - spacing * (_sprites.Count - 1) / 2;
        for (int i = 0; i < _sprites.Count; i++)
        {
            int hx = first + i * spacing;
            // A soft contact shadow, then the hero's idle loop offset per hero so the row does not move in step.
            for (int dx = -14; dx <= 14; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (dx * dx * 4 + dy * dy * 49 <= 14 * 14 * 4) c.Blend(hx + dx, feet + dy, Rgb(0x5A, 0x60, 0x80), 0.18f);
            DrawHero(c, _sprites[i], time + i * 0.37f, hx, feet);
        }
    }

    private void DrawHero(PixelCanvas c, AtlasSprite s, float time, int x, int y)
    {
        int frame = 0;
        if (s.Anims.TryGetValue("idle", out var idle) && idle.Frames.Count > 0)
            frame = idle.Frames[(int)MathF.Floor(time * idle.Fps) % idle.Frames.Count];
        int fx = s.Frames[frame][0], fy = s.Frames[frame][1];
        int dx = x - s.Origin[0] * Scale, dy = y - s.Origin[1] * Scale;
        for (int py = 0; py < s.H; py++)
            for (int px = 0; px < s.W; px++)
            {
                uint color = _atlas![fx + px, fy + py];
                if (RgbaImage.A(color) == 0) continue;
                for (int sy = 0; sy < Scale; sy++)
                    for (int sx = 0; sx < Scale; sx++)
                        c.Set(dx + px * Scale + sx, dy + py * Scale + sy, color);
            }
    }

    private static void Triangle(PixelCanvas c, int x0, int y0, int w, int h, Shard s)
    {
        int minX = (int)MathF.Floor(MathF.Min(s.X0, MathF.Min(s.X1, s.X2))), maxX = (int)MathF.Ceiling(MathF.Max(s.X0, MathF.Max(s.X1, s.X2)));
        int minY = (int)MathF.Floor(MathF.Min(s.Y0, MathF.Min(s.Y1, s.Y2))), maxY = (int)MathF.Ceiling(MathF.Max(s.Y0, MathF.Max(s.Y1, s.Y2)));
        minX = Math.Max(minX, 0); maxX = Math.Min(maxX, w - 1);
        minY = Math.Max(minY, 0); maxY = Math.Min(maxY, h - 1);
        float area = Edge(s.X0, s.Y0, s.X1, s.Y1, s.X2, s.Y2);
        if (MathF.Abs(area) < 1) return;
        uint light = Mix(s.Color, Rgb(0xFF, 0xFF, 0xFF), 0.55f);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float a = Edge(s.X1, s.Y1, s.X2, s.Y2, px, py) / area, b = Edge(s.X2, s.Y2, s.X0, s.Y0, px, py) / area;
                float g = 1 - a - b;
                if (a < 0 || b < 0 || g < 0) continue;
                // The facet nearest the first edge catches light; a 1px bright rim reads as cut crystal.
                bool rim = a < 0.04f || b < 0.04f;
                c.Blend(x0 + x, y0 + y, rim ? light : b > a ? s.Color : Mix(s.Color, light, 0.4f), s.Alpha);
            }
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    private static void DrawSparkles(PixelCanvas c, int x0, int y0, int w, int h, float time)
    {
        for (uint i = 0; i < 22; i++)
        {
            uint a = Hash(i + 4001), b = Hash(i + 5001);
            int x = x0 + (int)(a % (uint)w);
            bool top = (b & 1) == 0;
            int y = y0 + (top ? (int)(b % 90) : h - 1 - (int)(b % 80));
            float pulse = MathF.Max(0, MathF.Sin(time * 1.6f + i * 2.1f));
            if (pulse < 0.2f) continue;
            uint color = Rgb(0xFF, 0xFF, 0xFF);
            c.Blend(x, y, color, pulse);
            if (pulse > 0.75f)
            {
                c.Blend(x - 1, y, color, pulse * 0.6f);
                c.Blend(x + 1, y, color, pulse * 0.6f);
                c.Blend(x, y - 1, color, pulse * 0.6f);
                c.Blend(x, y + 1, color, pulse * 0.6f);
            }
        }
    }
}
