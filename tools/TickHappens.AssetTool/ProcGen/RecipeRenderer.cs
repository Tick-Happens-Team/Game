using TickHappens.Engine.Assets;

namespace TickHappens.AssetTool.ProcGen;

/// <summary>
/// Renders a <see cref="Recipe"/> natively on the pixel grid:
/// rasterize primitives (pixel-center sampling, no anti-aliasing) → normals from geometry →
/// directional light quantized into the material's palette ramp → contact shadows → decals →
/// selective 1px outline → cleanup. Also emits a normal map from the same geometry.
/// </summary>
public sealed class RecipeRenderer
{
    private readonly Recipe _r;
    private readonly Palette _pal;
    private readonly int _w, _h;
    private readonly float _lx, _ly, _lz;
    private readonly Dictionary<string, uint[]> _ramps = [];
    private readonly uint _ink;

    public RecipeRenderer(Recipe recipe, Palette palette, int? seedOverride = null)
    {
        _r = recipe;
        _pal = palette;
        _w = recipe.Size[0];
        _h = recipe.Size[1];
        float l = MathF.Sqrt(recipe.Light[0] * recipe.Light[0] + recipe.Light[1] * recipe.Light[1] + recipe.Light[2] * recipe.Light[2]);
        (_lx, _ly, _lz) = (recipe.Light[0] / l, recipe.Light[1] / l, recipe.Light[2] / l);
        int seed = seedOverride ?? recipe.Seed;
        foreach (var (name, m) in recipe.Materials)
        {
            var ramp = m.Ramp;
            if (m.Variants is { Count: > 0 })
            {
                var all = new List<List<string>> { m.Ramp };
                all.AddRange(m.Variants);
                ramp = all[(((seed - 1) % all.Count) + all.Count) % all.Count]; // seed 1 = primary ramp, then variants in order
            }
            _ramps[name] = [.. ramp.Select(h => Color(h, $"material '{name}'"))];
        }
        _ink = Color(recipe.Outline.Color, "outline");
    }

    private uint Color(string hex, string where)
    {
        uint c = Palette.ParseHex(hex);
        if (!_pal.Contains(c)) throw new InvalidDataException($"{_r.Sprite}: {where} color {hex} is not in palette {_pal.Name}");
        return c | 0xFF000000;
    }

    public SpriteSheet Render(string source)
    {
        var origin = _r.Origin ?? [_w / 2, _h - 1];
        var sheet = new SpriteSheet { Name = _r.Sprite, W = _w, H = _h, Origin = origin, Source = source };
        var seen = new Dictionary<string, int>();
        foreach (var (animName, anim) in _r.Anims)
        {
            var indices = new List<int>();
            for (int i = 0; i < anim.Frames.Count; i++)
            {
                var (color, emit, normal) = RenderFrame(anim.Frames[i]);
                string key = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(color.Px.AsSpan())));
                if (!seen.TryGetValue(key, out int idx))
                {
                    idx = sheet.Frames.Count;
                    seen[key] = idx;
                    sheet.Frames.Add(new SpriteFrame($"{animName}_{i}", color, emit));
                    sheet.Normals.Add(normal);
                }
                indices.Add(idx);
            }
            sheet.Anims[animName] = new SpriteAnim { Fps = anim.Fps, Loop = anim.Loop, Frames = indices };
        }
        return sheet;
    }

    private (float X, float Y, float Sx, float Sy) Bone(string name, FrameSpec f, int depth = 0)
    {
        if (depth > 16) throw new InvalidDataException($"{_r.Sprite}: bone cycle at '{name}'");
        if (!_r.Bones.TryGetValue(name, out var b))
        {
            if (name == "root") return (0, 0, 1, 1);
            throw new InvalidDataException($"{_r.Sprite}: unknown bone '{name}'");
        }
        var (px, py) = b.Parent != null ? (Bone(b.Parent, f, depth + 1) is var p ? (p.X, p.Y) : (0, 0)) : (0f, 0f);
        float x = px + b.At[0], y = py + b.At[1], sx = 1, sy = 1;
        if (f.Bones.TryGetValue(name, out var o))
        {
            x += o[0];
            y += o[1];
            if (o.Length >= 4) { sx = o[2]; sy = o[3]; }
        }
        return (x, y, sx, sy);
    }

    public (RgbaImage Color, RgbaImage Emission, RgbaImage Normal) RenderFrame(FrameSpec f)
    {
        int n = _w * _h;
        var part = new int[n];
        Array.Fill(part, -1);
        var nx = new float[n];
        var ny = new float[n];
        var nz = new float[n];
        var parts = new List<PartSpec>();
        foreach (var p in _r.Parts)
        {
            if (f.Hide.Contains(p.Name)) continue;
            var q = f.Parts.TryGetValue(p.Name, out var o) ? p.With(o) : p;
            parts.Add(q);
        }
        // Painter's order by z, stable by declaration order.
        var order = Enumerable.Range(0, parts.Count).OrderBy(i => parts[i].Z).ThenBy(i => i).ToList();
        var mask = new bool[n];
        foreach (int pi in order)
        {
            var p = parts[pi];
            if (!_ramps.ContainsKey(p.Mat)) throw new InvalidDataException($"{_r.Sprite}: part '{p.Name}' uses unknown material '{p.Mat}'");
            var bone = Bone(p.Bone, f);
            for (int copy = 0; copy < (p.Mirror ? 2 : 1); copy++)
            {
                Array.Clear(mask);
                float mirror = copy == 1 ? -1 : 1;
                Rasterize(p, bone, mirror, mask, out var normalFn);
                var bevel = p.Profile == "bevel" ? BevelNormals(mask, 2.5f) : null;
                for (int i = 0; i < n; i++)
                {
                    if (!mask[i]) continue;
                    part[i] = pi;
                    var (a, b2, c) = bevel != null ? (bevel[i * 3], bevel[i * 3 + 1], bevel[i * 3 + 2]) : normalFn(i % _w + 0.5f, i / _w + 0.5f);
                    nx[i] = a; ny[i] = b2; nz[i] = c;
                }
            }
        }

        // Sprite-level lighting: blend each part's normal with the bevel of the whole silhouette,
        // so a part on the shadow side of the body cannot pick up its own top-left highlight.
        if (_r.Unify > 0)
        {
            var all = new bool[n];
            for (int i = 0; i < n; i++) all[i] = part[i] >= 0;
            var g = BevelNormals(all, 3.5f);
            for (int i = 0; i < n; i++)
            {
                if (!all[i]) continue;
                float u = _r.Unify;
                float a = nx[i] * (1 - u) + g[i * 3] * u, b2 = ny[i] * (1 - u) + g[i * 3 + 1] * u, c = nz[i] * (1 - u) + g[i * 3 + 2] * u;
                float l = MathF.Sqrt(a * a + b2 * b2 + c * c);
                nx[i] = a / l; ny[i] = b2 / l; nz[i] = c / l;
            }
        }

        // Shade.
        var color = new RgbaImage(_w, _h);
        var emit = new RgbaImage(_w, _h);
        var protectedPx = new bool[n];
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                int pi = part[i];
                if (pi < 0) continue;
                var p = parts[pi];
                var m = _r.Materials[p.Mat];
                var ramp = _ramps[p.Mat];
                float d = nx[i] * _lx + ny[i] * _ly + nz[i] * _lz;
                float t = d * 0.5f + 0.5f;
                int band = 0;
                foreach (float th in m.Bands)
                {
                    if (m.Dither > 0 && MathF.Abs(t - th) < m.Dither)
                    {
                        // Inside the dither zone: Bayer threshold decides which side of the band this pixel takes.
                        float k = (t - (th - m.Dither)) / (2 * m.Dither);
                        if (k > BayerT(x, y)) band++;
                    }
                    else if (t > th) band++;
                }
                // Contact shadow from a part in front, above or to the left (light comes from top-left).
                if (Shadowed(x, y, pi, part, parts)) band--;
                int idx = Math.Clamp(m.Base + band, 0, ramp.Length - 1);
                if (m.Spec > 0 && d > m.Spec) idx = ramp.Length - 1;
                color[x, y] = ramp[idx];
                if (m.Emissive) emit[x, y] = ramp[idx];
            }

        // Decals (eyes, emblems, stitches): stamped pixels, protected from cleanup.
        foreach (var dcl in _r.Decals)
        {
            if (f.HideDecals.Contains(dcl.Name)) continue;
            var bone = Bone(dcl.Bone, f);
            for (int copy = 0; copy < (dcl.Mirror ? 2 : 1); copy++)
                for (int ry = 0; ry < dcl.Rows.Count; ry++)
                {
                    string row = dcl.Rows[ry];
                    for (int rx = 0; rx < row.Length; rx++)
                    {
                        char ch = row[rx];
                        if (ch == '.') continue;
                        if (!dcl.Keys.TryGetValue(ch.ToString(), out var hex)) throw new InvalidDataException($"{_r.Sprite}: decal '{dcl.Name}' key '{ch}' undefined");
                        int px = (int)MathF.Floor(bone.X) + dcl.At[0] + rx, py = (int)MathF.Floor(bone.Y) + dcl.At[1] + ry;
                        if (copy == 1) px = 2 * (int)MathF.Floor(bone.X) - 1 - px;
                        if (px < 0 || py < 0 || px >= _w || py >= _h) continue;
                        uint c = Color(hex, $"decal '{dcl.Name}'");
                        color[px, py] = c;
                        protectedPx[py * _w + px] = true;
                        if (dcl.Emissive.Contains(ch.ToString())) emit[px, py] = c;
                        if (part[py * _w + px] < 0) { nx[py * _w + px] = 0; ny[py * _w + px] = 0; nz[py * _w + px] = 1; }
                    }
                }
        }

        PixelCleanup.PruneSpurs(color, protectedPx);
        PixelCleanup.RemoveOrphanTones(color, protectedPx);
        PixelCleanup.Despeckle(color, 3, protectedPx);
        PixelCleanup.FillHoles(color);
        AddOutline(color, part, parts);
        // Emission only where the final sprite is opaque.
        for (int i = 0; i < n; i++) if (RgbaImage.A(color.Px[i]) == 0) emit.Px[i] = 0;

        var normal = new RgbaImage(_w, _h);
        for (int i = 0; i < n; i++)
        {
            if (RgbaImage.A(color.Px[i]) == 0) continue;
            float a = nx[i], b = -ny[i], c = nz[i];
            if (part[i] < 0) { a = 0; b = 0; c = 1; }
            normal.Px[i] = RgbaImage.Pack((int)MathF.Round((a * 0.5f + 0.5f) * 255), (int)MathF.Round((b * 0.5f + 0.5f) * 255), (int)MathF.Round((c * 0.5f + 0.5f) * 255));
        }
        return (color, emit, normal);
    }

    private static readonly int[] Bayer4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    private static float BayerT(int x, int y) => (Bayer4[((y & 3) << 2) | (x & 3)] + 0.5f) / 16f;

    private bool Shadowed(int x, int y, int pi, int[] part, List<PartSpec> parts)
    {
        float z = parts[pi].Z;
        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0) })
        {
            int xx = x + dx, yy = y + dy;
            if (xx < 0 || yy < 0) continue;
            int q = part[yy * _w + xx];
            if (q >= 0 && q != pi && parts[q].Z > z && parts[q].CastShadow && parts[q].Mat != parts[pi].Mat) return true;
        }
        return false;
    }

    private delegate (float X, float Y, float Z) NormalFn(float px, float py);

    private void Rasterize(PartSpec p, (float X, float Y, float Sx, float Sy) bone, float mirror, bool[] mask, out NormalFn normal)
    {
        float S(float v, float s) => v * s;
        switch (p.Shape)
        {
            case "ellipse":
            {
                float cx = bone.X + mirror * S(p.C[0], bone.Sx), cy = bone.Y + S(p.C[1], bone.Sy);
                float rx = MathF.Max(0.5f, p.R[0] * bone.Sx), ry = MathF.Max(0.5f, p.R[1] * bone.Sy);
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                        if (dx * dx + dy * dy <= 1) mask[y * _w + x] = true;
                    }
                normal = (px, py) => Profile(p.Profile, (px - cx) / rx, (py - cy) / ry);
                break;
            }
            case "rect":
            {
                float w = p.R[0] * bone.Sx, h = p.R[1] * bone.Sy;
                float x0 = mirror > 0 ? bone.X + S(p.C[0], bone.Sx) : bone.X - S(p.C[0], bone.Sx) - w;
                float y0 = bone.Y + S(p.C[1], bone.Sy);
                float rr = p.Round;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        if (px < x0 || py < y0 || px > x0 + w || py > y0 + h) continue;
                        if (rr > 0)
                        {
                            float qx = MathF.Max(MathF.Max(x0 + rr - px, px - (x0 + w - rr)), 0);
                            float qy = MathF.Max(MathF.Max(y0 + rr - py, py - (y0 + h - rr)), 0);
                            if (qx * qx + qy * qy > rr * rr) continue;
                        }
                        mask[y * _w + x] = true;
                    }
                float cx = x0 + w / 2, cy = y0 + h / 2;
                normal = (px, py) => Profile(p.Profile, (px - cx) / (w / 2), (py - cy) / (h / 2));
                break;
            }
            case "poly":
            {
                var pts = (p.Pts ?? throw new InvalidDataException($"{_r.Sprite}: poly '{p.Name}' needs pts"))
                    .Select(v => (X: bone.X + mirror * v[0] * bone.Sx, Y: bone.Y + v[1] * bone.Sy)).ToArray();
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                        if (Inside(pts, x + 0.5f, y + 0.5f)) mask[y * _w + x] = true;
                float minX = pts.Min(q => q.X), maxX = pts.Max(q => q.X), minY = pts.Min(q => q.Y), maxY = pts.Max(q => q.Y);
                float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, hw = MathF.Max(0.5f, (maxX - minX) / 2), hh = MathF.Max(0.5f, (maxY - minY) / 2);
                normal = (px, py) => Profile(p.Profile, (px - cx) / hw, (py - cy) / hh);
                break;
            }
            case "line":
            {
                var pts = (p.Pts ?? throw new InvalidDataException($"{_r.Sprite}: line '{p.Name}' needs pts"))
                    .Select(v => (X: bone.X + mirror * v[0] * bone.Sx, Y: bone.Y + v[1] * bone.Sy)).ToArray();
                float half = MathF.Max(0.5f, p.R[0] / 2);
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                        if (DistToPolyline(pts, x + 0.5f, y + 0.5f, out _) <= half) mask[y * _w + x] = true;
                normal = (px, py) =>
                {
                    float d = DistToPolyline(pts, px, py, out var near);
                    float ox = (px - near.X) / half, oy = (py - near.Y) / half;
                    return Profile("dome", ox, oy);
                };
                break;
            }
            default: throw new InvalidDataException($"{_r.Sprite}: unknown shape '{p.Shape}'");
        }
    }

    private static (float, float, float) Profile(string profile, float u, float v)
    {
        switch (profile)
        {
            case "flat": return (0, 0, 1);
            case "cyl":
            {
                u = Math.Clamp(u, -1, 1);
                return (u, 0, MathF.Sqrt(1 - u * u));
            }
            default: // dome
            {
                float d2 = u * u + v * v;
                if (d2 > 1) { float d = MathF.Sqrt(d2); u /= d; v /= d; d2 = 1; }
                // Slightly flattened dome: reads as volume without "pillow" rims.
                float z = MathF.Sqrt(1 - d2) + 0.25f;
                float l = MathF.Sqrt(u * u + v * v + z * z);
                return (u / l, v / l, z / l);
            }
        }
    }

    private float[] BevelNormals(bool[] mask, float bevel)
    {
        int n = _w * _h;
        var dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = mask[i] ? bevel : 0;
        for (int pass = 0; pass < (int)MathF.Ceiling(bevel); pass++)
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    if (!mask[i]) continue;
                    float m = dist[i];
                    m = MathF.Min(m, (x > 0 ? dist[i - 1] : 0) + 1);
                    m = MathF.Min(m, (x < _w - 1 ? dist[i + 1] : 0) + 1);
                    m = MathF.Min(m, (y > 0 ? dist[i - _w] : 0) + 1);
                    m = MathF.Min(m, (y < _h - 1 ? dist[i + _w] : 0) + 1);
                    dist[i] = m;
                }
        float H(int x, int y) => x < 0 || y < 0 || x >= _w || y >= _h ? 0 : dist[y * _w + x] / bevel;
        var r = new float[n * 3];
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                if (!mask[i]) continue;
                float gx = H(x + 1, y) - H(x - 1, y), gy = H(x, y + 1) - H(x, y - 1);
                float a = -gx * 1.4f, b = -gy * 1.4f, c = 1;
                float l = MathF.Sqrt(a * a + b * b + c * c);
                r[i * 3] = a / l; r[i * 3 + 1] = b / l; r[i * 3 + 2] = c / l;
            }
        return r;
    }

    private static bool Inside((float X, float Y)[] pts, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                inside = !inside;
        return inside;
    }

    private static float DistToPolyline((float X, float Y)[] pts, float x, float y, out (float X, float Y) nearest)
    {
        float best = float.MaxValue;
        nearest = pts[0];
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var (ax, ay) = pts[i];
            var (bx, by) = pts[i + 1];
            float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / l2, 0, 1);
            float qx = ax + dx * t, qy = ay + dy * t, d = MathF.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
            if (d < best) { best = d; nearest = (qx, qy); }
        }
        return best;
    }

    /// <summary>1px outline outside the silhouette (4-neighbour). Selective: lit edges take the material's darkest tone.</summary>
    private void AddOutline(RgbaImage img, int[] part, List<PartSpec> parts)
    {
        var src = (uint[])img.Px.Clone();
        bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h && RgbaImage.A(src[y * _w + x]) != 0;
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                if (Opaque(x, y)) continue;
                bool below = Opaque(x, y + 1), right = Opaque(x + 1, y), above = Opaque(x, y - 1), left = Opaque(x - 1, y);
                if (!(below || right || above || left)) continue;
                uint c = _ink;
                // Lit side: this outline pixel sits above or left of the body (the body is below/right of it).
                if (_r.Outline.Selout && (below || right) && !above && !left)
                {
                    int bx = below ? x : x + 1, by = below ? y + 1 : y;
                    int q = part[by * _w + bx];
                    if (q >= 0) c = _ramps[parts[q].Mat][0];
                }
                img[x, y] = c;
            }
    }
}
