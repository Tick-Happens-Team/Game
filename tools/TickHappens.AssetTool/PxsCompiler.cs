using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool;

/// <summary>
/// Compiles hand-authored indexed pixel sources (.pxs). Format, one directive per line, '#' comments:
/// <code>
/// sprite NAME
/// size W H
/// origin X Y              pivot inside the frame (defaults to bottom-center)
/// mirror                  rows give the left half; the right half is mirrored (W must be even)
/// key C RRGGBB            map a character to a palette color ('.' is always transparent)
/// emissive C C ...        keys whose pixels also go to the emission map
/// frame NAME [from BASE] [shift DX DY]
///   ROW ...               H full rows, or (with from) 'row N ROW' overrides
/// end
/// anim NAME FPS FRAME FRAME ...   append '!' after FPS for non-looping
/// </code>
/// </summary>
public static class PxsCompiler
{
    public static SpriteSheet Compile(string path, Palette palette)
    {
        var lines = File.ReadAllLines(path);
        string? name = null;
        int w = 0, h = 0;
        int[]? origin = null;
        bool mirror = false;
        var keys = new Dictionary<char, uint> { ['.'] = 0 };
        var emissive = new HashSet<char>();
        var frames = new Dictionary<string, char[][]>();
        var order = new List<string>();
        var anims = new List<(string Name, float Fps, bool Loop, string[] Frames)>();

        string Err(int ln, string msg) => $"{path}:{ln + 1}: {msg}";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]);
            if (line.Length == 0) continue;
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "sprite": name = t[1]; break;
                case "size": w = int.Parse(t[1]); h = int.Parse(t[2]); break;
                case "origin": origin = [int.Parse(t[1]), int.Parse(t[2])]; break;
                case "mirror":
                    mirror = true;
                    if (w % 2 != 0) throw new InvalidDataException(Err(i, "mirror needs an even width"));
                    break;
                case "key":
                {
                    if (t[1].Length != 1) throw new InvalidDataException(Err(i, "key must be one character"));
                    uint c = Palette.ParseHex(t[2]);
                    if (!palette.Contains(c)) throw new InvalidDataException(Err(i, $"color {t[2]} is not in palette {palette.Name}"));
                    keys[t[1][0]] = c | 0xFF000000;
                    break;
                }
                case "emissive":
                    foreach (var k in t.Skip(1)) emissive.Add(k[0]);
                    break;
                case "frame":
                {
                    if (w == 0 || h == 0) throw new InvalidDataException(Err(i, "size must come before frames"));
                    string fname = t[1];
                    string? from = null;
                    int dx = 0, dy = 0;
                    for (int j = 2; j < t.Length; j++)
                    {
                        if (t[j] == "from") from = t[++j];
                        else if (t[j] == "shift") { dx = int.Parse(t[++j]); dy = int.Parse(t[++j]); }
                        else throw new InvalidDataException(Err(i, $"unexpected '{t[j]}'"));
                    }
                    int rowW = mirror ? w / 2 : w;
                    char[][] grid;
                    if (from != null)
                    {
                        if (!frames.TryGetValue(from, out var b)) throw new InvalidDataException(Err(i, $"unknown base frame '{from}'"));
                        grid = Shift(b, w, h, dx, dy);
                    }
                    else grid = [.. Enumerable.Range(0, h).Select(_ => Enumerable.Repeat('.', w).ToArray())];

                    int row = 0;
                    for (i++; i < lines.Length; i++)
                    {
                        var l = StripComment(lines[i]);
                        if (l.Length == 0) continue;
                        if (l == "end") break;
                        string data;
                        int y;
                        if (l.StartsWith("row "))
                        {
                            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            y = int.Parse(p[1]);
                            data = p[2];
                        }
                        else
                        {
                            if (from != null) throw new InvalidDataException(Err(i, "derived frames only take 'row N ...' overrides"));
                            y = row++;
                            data = l;
                        }
                        if (y < 0 || y >= h) throw new InvalidDataException(Err(i, $"row {y} out of range"));
                        if (data.Length != rowW) throw new InvalidDataException(Err(i, $"row has {data.Length} pixels, expected {rowW}"));
                        for (int x = 0; x < rowW; x++)
                        {
                            if (!keys.ContainsKey(data[x])) throw new InvalidDataException(Err(i, $"undefined key '{data[x]}'"));
                            grid[y][x] = data[x];
                            if (mirror) grid[y][w - 1 - x] = data[x];
                        }
                    }
                    if (from == null && row != h) throw new InvalidDataException(Err(i, $"frame '{fname}' has {row} rows, expected {h}"));
                    frames[fname] = grid;
                    order.Add(fname);
                    break;
                }
                case "anim":
                {
                    bool loop = !t[2].EndsWith('!');
                    anims.Add((t[1], float.Parse(t[2].TrimEnd('!'), System.Globalization.CultureInfo.InvariantCulture), loop, t[3..]));
                    break;
                }
                default: throw new InvalidDataException(Err(i, $"unknown directive '{t[0]}'"));
            }
        }

        if (name == null) throw new InvalidDataException($"{path}: missing 'sprite'");
        var sheet = new SpriteSheet { Name = name, W = w, H = h, Origin = origin ?? [w / 2, h - 1], Source = path };
        foreach (var fname in order)
        {
            var grid = frames[fname];
            var color = new RgbaImage(w, h);
            var emit = new RgbaImage(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = grid[y][x];
                    uint c = keys[ch];
                    color[x, y] = c;
                    if (emissive.Contains(ch)) emit[x, y] = c;
                }
            sheet.Frames.Add(new SpriteFrame(fname, color, emit));
            sheet.Normals.Add(null);
        }
        foreach (var (an, fps, loop, fr) in anims)
            sheet.Anims[an] = new SpriteAnim { Fps = fps, Loop = loop, Frames = [.. fr.Select(sheet.FrameIndex)] };
        return sheet;
    }

    private static string StripComment(string s)
    {
        int i = s.IndexOf('#');
        // '#' is a comment only at line start or after whitespace (keys may not use '#').
        return (i >= 0 ? s[..i] : s).Trim();
    }

    private static char[][] Shift(char[][] src, int w, int h, int dx, int dy)
    {
        var g = Enumerable.Range(0, h).Select(_ => Enumerable.Repeat('.', w).ToArray()).ToArray();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx, sy = y - dy;
                if (sx >= 0 && sy >= 0 && sx < w && sy < h) g[y][x] = src[sy][sx];
            }
        return g;
    }
}
