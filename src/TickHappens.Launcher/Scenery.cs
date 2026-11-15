using TickHappens.Engine.Vfx;
using static TickHappens.Launcher.Paint;

namespace TickHappens.Launcher;

/// <summary>
/// Procedural dusk banner for the play page: banded sky with ordered dithering, stars, a moon, layered hills
/// with pines, a clock tower whose hands follow the launcher's clock, and drifting fireflies. Drawn entirely in
/// code because the launcher runs before the Assets pack is installed.
/// </summary>
internal static class Scenery
{
    private static readonly uint[] Sky =
    [
        Rgb(0x16, 0x10, 0x2C), Rgb(0x1F, 0x15, 0x3A), Rgb(0x2C, 0x1A, 0x47), Rgb(0x42, 0x20, 0x52),
        Rgb(0x60, 0x27, 0x58), Rgb(0x86, 0x33, 0x58), Rgb(0xB0, 0x48, 0x55), Rgb(0xD8, 0x6E, 0x55),
        Rgb(0xF2, 0x9C, 0x62),
    ];

    private static readonly int[,] Bayer = { { 0, 2 }, { 3, 1 } };

    public static void Draw(PixelCanvas c, int x0, int y0, int w, int h, float time)
    {
        int ground = y0 + h - 34;
        DrawSky(c, x0, y0, w, ground - y0 + 1);
        DrawStars(c, x0, y0, w, (ground - y0) * 3 / 5, time);
        DrawMoon(c, x0 + w * 4 / 5, y0 + 46);
        DrawHills(c, x0, w, ground, 64, 0.021f, 1.7f, Rgb(0x4A, 0x24, 0x4C), 11);
        DrawHills(c, x0, w, ground, 40, 0.034f, 0.4f, Rgb(0x30, 0x19, 0x36), 23);
        DrawPines(c, x0, w, ground, Rgb(0x22, 0x12, 0x27));
        DrawTower(c, x0 + w * 2 / 7, ground, time);
        DrawGround(c, x0, w, ground, y0 + h);
        DrawFireflies(c, x0, y0, w, ground, time);
    }

    private static void DrawSky(PixelCanvas c, int x0, int y0, int w, int h)
    {
        int bands = Sky.Length;
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)Math.Max(1, h - 1) * (bands - 1);
            int band = Math.Min((int)t, bands - 2);
            float fraction = t - band;
            for (int x = 0; x < w; x++)
            {
                // Two-step dither near each band boundary keeps the 16-bit banding readable.
                float threshold = (Bayer[y & 1, x & 1] + 0.5f) / 4f;
                bool next = fraction > 0.65f && (fraction - 0.65f) / 0.35f > threshold;
                c.Set(x0 + x, y0 + y, Sky[next ? band + 1 : band]);
            }
        }
    }

    private static void DrawStars(PixelCanvas c, int x0, int y0, int w, int h, float time)
    {
        for (uint i = 0; i < 90; i++)
        {
            uint a = Hash(i * 3 + 1), b = Hash(i * 3 + 2), d = Hash(i * 3 + 3);
            int x = x0 + (int)(a % (uint)w), y = y0 + (int)(b % (uint)Math.Max(1, h));
            float twinkle = 0.5f + 0.5f * MathF.Sin(time * (1.2f + (d & 7) * 0.3f) + (d >> 8) % 628 / 100f);
            uint color = (d & 3) == 0 ? Rgb(0xFF, 0xE6, 0xB0) : Rgb(0xDD, 0xD8, 0xFF);
            c.Blend(x, y, color, 0.35f + 0.65f * twinkle);
            if ((d & 15) == 0 && twinkle > 0.8f)
            {
                c.Blend(x - 1, y, color, 0.4f);
                c.Blend(x + 1, y, color, 0.4f);
                c.Blend(x, y - 1, color, 0.4f);
                c.Blend(x, y + 1, color, 0.4f);
            }
        }
    }

    private static void DrawMoon(PixelCanvas c, int cx, int cy)
    {
        for (int r = 30; r > 17; r -= 3) c.AlphaDisc(cx, cy, r, Rgb(0xFF, 0xD8, 0xC0), 0.06f);
        c.Disc(cx, cy, 16, Rgb(0xFF, 0xF0, 0xD6));
        // Terminator shading on the lower-left edge.
        for (int y = -16; y <= 16; y++)
            for (int x = -16; x <= 16; x++)
            {
                int d = x * x + y * y;
                if (d <= 256 && (x - 5) * (x - 5) + (y - 5) * (y - 5) > 300) c.Set(cx + x, cy + y, Rgb(0xE0, 0xC4, 0xB4));
            }
        c.Disc(cx - 5, cy - 3, 3, Rgb(0xE8, 0xD2, 0xBE));
        c.Disc(cx + 4, cy + 5, 2, Rgb(0xE8, 0xD2, 0xBE));
        c.Disc(cx + 6, cy - 6, 1.5f, Rgb(0xE8, 0xD2, 0xBE));
    }

    private static void DrawHills(PixelCanvas c, int x0, int w, int ground, int height, float frequency, float phase, uint color, uint seed)
    {
        for (int x = 0; x < w; x++)
        {
            float wave = MathF.Sin(x * frequency + phase) * 0.5f + MathF.Sin(x * frequency * 2.3f + phase * 3) * 0.25f;
            int top = ground - (int)(height * (0.55f + 0.45f * wave));
            for (int y = top; y <= ground; y++) c.Set(x0 + x, y, color);
            // Rim light from the setting sun.
            if ((Hash((uint)x + seed) & 3) != 0) c.Set(x0 + x, top, Mix(color, Rgb(0xF2, 0x9C, 0x62), 0.35f));
        }
    }

    private static void DrawPines(PixelCanvas c, int x0, int w, int ground, uint color)
    {
        for (uint i = 0; i < 26; i++)
        {
            uint a = Hash(i + 101);
            int x = x0 + (int)(a % (uint)w), height = 14 + (int)((a >> 12) % 18);
            for (int y = 0; y < height; y++)
            {
                int half = (y * 5 / height) + (y % 4 == 3 ? 0 : 1);
                for (int dx = -half; dx <= half; dx++) c.Set(x + dx, ground - height + y, color);
            }
        }
    }

    private static void DrawTower(PixelCanvas c, int cx, int ground, float time)
    {
        uint stone = Rgb(0x2A, 0x1A, 0x30), stoneLight = Rgb(0x45, 0x2C, 0x4A), stoneDark = Rgb(0x1A, 0x10, 0x1E);
        int width = 34, top = ground - 132;
        Fill(c, cx - width / 2, top, width, ground - top, stone);
        Fill(c, cx - width / 2, top, 3, ground - top, stoneLight);
        Fill(c, cx + width / 2 - 4, top, 4, ground - top, stoneDark);
        // Brick courses.
        for (int y = top + 6; y < ground; y += 6)
            for (int x = cx - width / 2 + 3; x < cx + width / 2 - 4; x++)
                if ((x + y / 6 * 4) % 9 != 0) c.Set(x, y, stoneDark);
        // Ledge and spire.
        Fill(c, cx - width / 2 - 4, top - 4, width + 8, 5, stoneDark);
        Fill(c, cx - width / 2 - 4, top - 4, width + 8, 1, stoneLight);
        for (int y = 0; y < 34; y++)
        {
            int half = (width / 2 + 2) * (34 - y) / 34;
            for (int dx = -half; dx <= half; dx++) c.Set(cx + dx, top - 5 - y, dx < 0 ? stoneLight : stone);
        }
        c.Set(cx, top - 40, Gold);
        Fill(c, cx, top - 39, 1, 2, Gold);

        // Clock face with a warm glow; hands follow the launcher clock as a cosmetic touch.
        int fy = top + 24;
        for (int r = 22; r > 12; r -= 2) c.AlphaDisc(cx, fy, r, Gold, 0.07f);
        c.Disc(cx, fy, 12, Ink);
        c.Disc(cx, fy, 11, Rgb(0xFF, 0xE9, 0xB8));
        for (int i = 0; i < 12; i++)
        {
            float a = i * MathF.Tau / 12;
            c.Set(cx + MathF.Round(MathF.Sin(a) * 9), fy - MathF.Round(MathF.Cos(a) * 9), GoldDark);
        }
        float minute = time * MathF.Tau / 20, hour = minute / 12 + 1.2f;
        c.Line(cx, fy, cx + MathF.Sin(minute) * 8, fy - MathF.Cos(minute) * 8, Ink);
        c.Line(cx, fy, cx + MathF.Sin(hour) * 5, fy - MathF.Cos(hour) * 5, Rgb(0x8A, 0x2E, 0x3A));
        c.Set(cx, fy, Ink);

        // Lit windows and the door.
        for (int i = 0; i < 3; i++)
        {
            int wy = top + 52 + i * 26;
            bool lit = ((int)(time * 0.25f) + i) % 5 != 0;
            Fill(c, cx - 3, wy, 6, 9, Ink);
            Fill(c, cx - 2, wy + 1, 4, 7, lit ? Gold : stoneDark);
            if (lit) c.AlphaDisc(cx, wy + 4, 7, Gold, 0.12f);
        }
        Fill(c, cx - 6, ground - 16, 12, 16, Ink);
        Fill(c, cx - 5, ground - 15, 10, 15, Rgb(0x5A, 0x32, 0x28));
        c.Set(cx + 3, ground - 8, Gold);
    }

    private static void DrawGround(PixelCanvas c, int x0, int w, int ground, int bottom)
    {
        uint grass = Rgb(0x1E, 0x3A, 0x2C), grassDark = Rgb(0x14, 0x26, 0x20), grassLight = Rgb(0x3A, 0x62, 0x3E);
        Fill(c, x0, ground, w, bottom - ground, grass);
        for (int y = ground + 6; y < bottom; y++)
            for (int x = 0; x < w; x++)
                if (((x + y * 3) & 7) == 0 && (Hash((uint)(x * 977 + y)) & 3) == 0) c.Set(x0 + x, y, grassDark);
        for (int x = 0; x < w; x++)
        {
            uint a = Hash((uint)x + 7000);
            int tuft = (int)(a % 4);
            for (int y = 0; y < tuft; y++) c.Set(x0 + x, ground - y, (a & 16) != 0 ? grassLight : grass);
        }
        Fill(c, x0, ground, w, 1, grassLight);
    }

    private static void DrawFireflies(PixelCanvas c, int x0, int y0, int w, int ground, float time)
    {
        for (uint i = 0; i < 18; i++)
        {
            uint a = Hash(i + 501), b = Hash(i + 901);
            float speed = 0.3f + (a & 15) / 30f;
            float x = x0 + (a >> 4) % (uint)w + MathF.Sin(time * speed + i) * 14;
            float y = ground - 8 - (b % 70) + MathF.Sin(time * speed * 1.7f + i * 2) * 6;
            float pulse = 0.5f + 0.5f * MathF.Sin(time * 2.5f + i * 1.3f);
            if (y < y0 || x < x0 || x >= x0 + w) continue;
            c.Glow(x, y, 3, Rgb(0xC8, 0xFF, 0x8A), 0.25f * pulse);
            c.Blend(x, y, Rgb(0xF0, 0xFF, 0xC8), 0.4f + 0.6f * pulse);
        }
    }
}
