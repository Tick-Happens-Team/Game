using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;

namespace TickHappens.Launcher;

/// <summary>Palette and pixel primitives for the launcher UI. Colors are packed as 0xAABBGGRR like the game's UI layer.</summary>
internal static class Paint
{
    public static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)b << 16 | (uint)g << 8 | (uint)r;

    // Light box-cover palette: paper ground, dark plum ink, pale bevelled panels.
    public static readonly uint Paper = Rgb(0xF7, 0xF5, 0xF0);
    public static readonly uint White = Rgb(0xFF, 0xFF, 0xFF);
    public static readonly uint Ink = Rgb(0x2A, 0x22, 0x33);
    public static readonly uint Panel = Rgb(0xEA, 0xE6, 0xEE);
    public static readonly uint PanelLight = White;
    public static readonly uint PanelDark = Rgb(0xC9, 0xC1, 0xD2);
    public static readonly uint Text = Ink;
    public static readonly uint Muted = Rgb(0x85, 0x7C, 0x90);
    public static readonly uint Accent = Rgb(0x2E, 0x9E, 0x96);
    public static readonly uint Gold = Rgb(0xD9, 0x9A, 0x2B);
    public static readonly uint GoldDark = Rgb(0x9C, 0x64, 0x18);
    public static readonly uint Green = Rgb(0x5C, 0xC0, 0x4E);
    public static readonly uint GreenDark = Rgb(0x2E, 0x7A, 0x3A);
    public static readonly uint GreenLight = Rgb(0x9E, 0xE6, 0x7A);
    public static readonly uint Danger = Rgb(0xC0, 0x3F, 0x36);

    public static void Fill(PixelCanvas c, int x, int y, int w, int h, uint color)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                c.Set(i, j, color);
    }

    public static void Shade(PixelCanvas c, int x, int y, int w, int h, uint color, float alpha)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                c.Blend(i, j, color, alpha);
    }

    /// <summary>A bevelled 16-bit style panel: dark outline, light top-left edge, dark bottom-right edge.</summary>
    public static void Bevel(PixelCanvas c, int x, int y, int w, int h, uint face, uint light, uint dark)
    {
        Fill(c, x, y, w, h, Ink);
        Fill(c, x + 1, y + 1, w - 2, h - 2, face);
        Fill(c, x + 1, y + 1, w - 2, 1, light);
        Fill(c, x + 1, y + 1, 1, h - 2, light);
        Fill(c, x + 1, y + h - 2, w - 2, 1, dark);
        Fill(c, x + w - 2, y + 1, 1, h - 2, dark);
    }

    /// <summary>Text in the built-in pixel font. Characters the font lacks are replaced with close equivalents.</summary>
    public static void Label(PixelCanvas c, string text, int x, int y, uint color, int scale = 1) =>
        PixelFont.Draw(c, Printable(text), x, y, color, 0, scale);

    public static void Centered(PixelCanvas c, string text, int cx, int y, uint color, int scale = 1) =>
        PixelFont.DrawCentered(c, Printable(text), cx, y, color, 0, scale);

    public static int Measure(string text, int scale = 1) => PixelFont.Measure(Printable(text), scale);

    public static string Printable(string text)
    {
        if (text.Contains('…')) text = text.Replace("…", "...");
        return text.Replace('—', '-').Replace('–', '-').Replace('"', '\'').Replace('«', '\'').Replace('»', '\'');
    }

    /// <summary>Splits text into lines no wider than <paramref name="width"/> pixels.</summary>
    public static List<string> Wrap(string text, int width, int scale = 1)
    {
        var lines = new List<string>();
        foreach (string paragraph in Printable(text).Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length != 0 && PixelFont.Measure(next, scale) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = next;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Deterministic integer hash for scenery placement.</summary>
    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352D;
        x ^= x >> 15;
        x *= 0x846CA68B;
        x ^= x >> 16;
        return x;
    }

    public static uint Mix(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Channel(int shift) => (uint)MathF.Round(((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t);
        return 0xFF000000u | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
}
