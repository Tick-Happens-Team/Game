namespace TickHappens.Engine.Assets;

/// <summary>The project palette (Lospec .hex format: one RRGGBB per line). Colors are 0x00BBGGRR.</summary>
public sealed class Palette
{
    private readonly HashSet<uint> _set;
    private readonly (float L, float A, float B)[] _lab;

    public string Name { get; }
    public IReadOnlyList<uint> Colors { get; }

    public Palette(string name, IReadOnlyList<uint> colors)
    {
        Name = name;
        Colors = colors;
        _set = [.. colors];
        _lab = [.. colors.Select(OkLab)];
    }

    public static Palette Load(string path)
    {
        var colors = new List<uint>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim().TrimStart('#');
            if (line.Length == 0 || line.StartsWith(';')) continue;
            colors.Add(ParseHex(line));
        }
        return new Palette(Path.GetFileNameWithoutExtension(path), colors);
    }

    /// <summary>"RRGGBB" to 0x00BBGGRR.</summary>
    public static uint ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) throw new FormatException($"bad color '{hex}'");
        int v = Convert.ToInt32(hex, 16);
        return (uint)(((v & 255) << 16) | (v & 0xff00) | ((v >> 16) & 255));
    }

    public static string ToHex(uint c) => $"{c & 255:x2}{(c >> 8) & 255:x2}{(c >> 16) & 255:x2}";

    public bool Contains(uint rgb) => _set.Contains(rgb & 0x00FFFFFF);

    /// <summary>Nearest palette color by perceptual (OKLab) distance.</summary>
    public uint Nearest(uint rgb)
    {
        rgb &= 0x00FFFFFF;
        if (_set.Contains(rgb)) return rgb;
        var p = OkLab(rgb);
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < _lab.Length; i++)
        {
            float dl = p.L - _lab[i].L, da = p.A - _lab[i].A, db = p.B - _lab[i].B, d = dl * dl + da * da + db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        return Colors[best];
    }

    public static (float L, float A, float B) OkLab(uint c)
    {
        static float Lin(float v) { v /= 255f; return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f); }
        float r = Lin(c & 255), g = Lin((c >> 8) & 255), b = Lin((c >> 16) & 255);
        float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return (0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }
}
