// C# port of Sprite Fusion "sparkle" pixel VFX. MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt. Simulation units are the original's "screen" units (2 per art pixel).
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public sealed class SparkleOptions
{
    /// <summary>Center in simulation units (art pixels * 2).</summary>
    public float X = 740, Y = 480;
    /// <summary>Main flare arm length in art pixels.</summary>
    public float Size = 19;
    /// <summary>Seven-stop warm ramp, core to shadow. Null means the default warm palette.</summary>
    public uint[]? Warm;
    /// <summary>Eight-stop cool ramp, core to shadow. Null means the default cool palette.</summary>
    public uint[]? Cool;
    /// <summary>Number of satellite flares orbiting the main one.</summary>
    public float Flares = 11;
    public uint Seed = 1;
}

/// <summary>Twinkling star burst: a pulsing eight-armed flare, orbiting satellites and drifting motes.</summary>
public sealed class SparkleEffect : IPixelEffect
{
    internal const float PX = 2;

    private static readonly uint White = Hex("#ffffff");

    private static readonly uint[] WarmPalette =
        [Hex("#ffffff"), Hex("#fffbe2"), Hex("#fff0a6"), Hex("#ffd862"), Hex("#f6aa3c"), Hex("#cf722b"), Hex("#7e3c1f")];

    private static readonly uint[] CoolPalette =
        [Hex("#ffffff"), Hex("#dcfbff"), Hex("#a6ecff"), Hex("#62c8f4"), Hex("#3a8bd8"), Hex("#2c58aa"), Hex("#212f6b"), Hex("#151a3d")];

    private readonly uint[] _warm, _cool;
    private readonly VfxRng _r;
    private readonly Particles _ps;
    private readonly Sat[] _sats;
    private readonly float _x, _y, _cx, _cy, _size;
    private float _t;

    public SparkleEffect(SparkleOptions o)
    {
        _warm = o.Warm is { Length: > 0 } warm ? warm : WarmPalette;
        _cool = o.Cool is { Length: > 0 } cool ? cool : CoolPalette;
        _size = o.Size;
        _r = new VfxRng(o.Seed);
        _x = o.X;
        _y = o.Y;
        _cx = _x / PX;
        _cy = _y / PX;
        _ps = new Particles(_r);

        int n = Math.Max(0, (int)o.Flares);
        _sats = new Sat[n];
        for (int i = 0; i < n; i++)
        {
            float a = _r.Range(0, Tau), d = _r.Range(9, 32);
            _sats[i] = new Sat
            {
                X = _cx + MathF.Cos(a) * d,
                Y = _cy + MathF.Sin(a) * d * 0.85f,
                T0 = 0.1f + i * 0.17f + _r.Range(0, 0.08f),
                Dur = _r.Range(0.35f, 0.6f),
                L = _r.Range(4, 8.5f),
                Spin = _r.Range(-2, 2),
                Ice = _r.Chance(0.35f),
            };
        }
    }

    public bool Done => _t > 2.5f && _ps.List.Count == 0;

    public void Update(float dt)
    {
        _t += dt;
        if (_t < 2.3f && _r.Chance(0.35f))
        {
            float a = _r.Range(0, Tau), d = _r.Range(4, 40);
            _ps.Spawn(_x + MathF.Cos(a) * d * 2, _y + MathF.Sin(a) * d * 1.7f,
                _r.Range(-10, 10), _r.Range(-14, 6), _r.Range(0.5f, 0.9f),
                _r.Chance(0.5f) ? At(_warm, 1) : _r.Chance(0.5f) ? At(_cool, 2) : White,
                _r.Range(0.4f, 2.2f));
        }
        _ps.Update(dt);
    }

    public void Draw(PixelFrame f, VfxLayer layer)
    {
        if (layer == VfxLayer.Back) return;
        var buf = PixelCanvas.ColorOf(f);
        var light = PixelCanvas.LightOf(f);
        float grow = OutBack(Clamp(_t / 0.25f), 2), gone = 1 - Smooth(1.9f, 2.5f, _t);
        float pulse = 1 + 0.35f * MathF.Exp(-((_t - 0.9f) * (_t - 0.9f)) / 0.01f) + 0.3f * MathF.Exp(-((_t - 1.5f) * (_t - 1.5f)) / 0.01f);
        Flare(buf, light, _cx, _cy, _size * grow * gone * pulse, _t * 0.7f, At(_warm, 2), At(_warm, 3));
        if (grow * gone > 0.1f) light.Glow(_cx, _cy, 34 * gone, At(_warm, 2), 0.35f * gone);
        foreach (var s in _sats)
        {
            float u = (_t - s.T0) / s.Dur;
            if (u <= 0 || u >= 1) continue;
            Flare(buf, light, s.X, s.Y, s.L * MathF.Pow(MathF.Sin(u * MathF.PI), 0.7f), s.Spin * u + 0.4f,
                s.Ice ? At(_cool, 2) : At(_warm, 2), s.Ice ? At(_cool, 3) : At(_warm, 3));
        }
        for (int i = 0; i < 26; i++)
        {
            int fr = (int)MathF.Floor(_t * 12) + i * 3;
            if (Hash(fr, i) >= 0.3f * gone) continue;
            float a = Hash(i, 1) * Tau, d = 6 + Hash(i, 2) * 40;
            buf.Dot(_cx + MathF.Cos(a) * d, _cy + MathF.Sin(a) * d * 0.8f, Hash(fr, 5) < 0.5f ? White : At(_warm, 1));
        }
        _ps.Draw(buf, light);
    }

    // ---- star flare ------------------------------------------------------------------------
    private static void Flare(PixelCanvas buf, PixelCanvas light, float x, float y, float L, float ang, uint c1, uint c2, float a = 1)
    {
        if (L < 0.8f) return;
        for (int i = 0; i < 8; i++)
        {
            bool diag = i % 2 == 1;
            float an = ang + i * Tau / 8, len = diag ? L * 0.42f : L, ca = MathF.Cos(an), sa = MathF.Sin(an);
            for (float d = 0; d <= len; d += 0.5f)
            {
                float u = d / len;
                uint c = diag ? (u < 0.5f ? c1 : c2) : u < 0.3f ? White : u < 0.65f ? c1 : c2;
                buf.Dot(x + ca * d, y + sa * d, c, a * (u < 0.7f ? 1 : 1 - (u - 0.7f) / 0.35f));
                if (!diag && u < 0.22f && L > 4)
                {
                    buf.Dot(x + ca * d - sa, y + sa * d + ca, c1, a);
                    buf.Dot(x + ca * d + sa, y + sa * d - ca, c1, a);
                }
            }
        }
        buf.Dot(x, y, White, a);
        light.Glow(x, y, 4 + L * 1.4f, c1, 0.5f * a);
        light.Glow(x, y, 2 + L * 0.5f, White, 0.4f * a);
    }

    // ---- helpers ---------------------------------------------------------------------------
    /// <summary>JS Math.round: halves round up, unlike MathF.Round's banker's rounding.</summary>
    private static int Round(float v) => (int)MathF.Floor(v + 0.5f);

    private static uint At(uint[] r, int i) => r[Math.Clamp(i, 0, r.Length - 1)];

    private static float Smooth(float a, float b, float x)
    {
        float t = Clamp((x - a) / (b - a));
        return t * t * (3 - 2 * t);
    }

    private static float OutBack(float t, float s = 1.7f)
    {
        float u = Clamp(t) - 1;
        return 1 + (s + 1) * u * u * u + s * u * u;
    }

    // ---- satellites ------------------------------------------------------------------------
    private sealed class Sat
    {
        public float X, Y, T0, Dur, L, Spin;
        public bool Ice;
    }

    // ---- particles -------------------------------------------------------------------------
    private sealed class Part
    {
        public float X, Y, Vx, Vy, Life, Max, Size, Seed;
        public uint Col;
    }

    private sealed class Particles(VfxRng r)
    {
        public readonly List<Part> List = [];
        public float T;

        public void Spawn(float x, float y, float vx, float vy, float life, uint col, float size) =>
            List.Add(new Part { X = x, Y = y, Vx = vx, Vy = vy, Life = life, Max = life, Col = col, Size = size, Seed = r.Next() * 100 });

        public void Update(float dt)
        {
            T += dt;
            var L = List;
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var p = L[i];
                if ((p.Life -= dt) <= 0) { L.RemoveAt(i); continue; }
                p.Vx -= p.Vx * 1.6f * dt;
                p.Vy -= p.Vy * 1.6f * dt + 10 * dt;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
            }
        }

        public void Draw(PixelCanvas buf, PixelCanvas light)
        {
            foreach (var p in List)
            {
                float k = p.Life / p.Max, ax = p.X / PX, ay = p.Y / PX;
                int px = (int)MathF.Floor(ax), py = (int)MathF.Floor(ay);
                float tw = 0.6f + 0.4f * MathF.Sin(T * (8 + p.Seed % 5) + p.Seed);
                if (p.Size < 1)
                {
                    if (tw * k > 0.25f) buf.Set(px, py, tw > 0.8f ? White : p.Col);
                    light.Glow(ax, ay, 3, p.Col, 0.4f * k * tw);
                    continue;
                }
                int arm = Math.Max(1, Round(p.Size * tw * MathF.Min(1, k * 3)));
                uint tip = Scale(p.Col, 0.75f);
                for (int d = 1; d <= arm; d++)
                {
                    uint cc = d == arm && arm > 1 ? tip : p.Col;
                    buf.Set(px + d, py, cc);
                    buf.Set(px - d, py, cc);
                    buf.Set(px, py + d, cc);
                    buf.Set(px, py - d, cc);
                }
                if (arm >= 3)
                    for (int j = 0; j < 4; j++) buf.Set(px + ((j & 1) != 0 ? -1 : 1), py + ((j & 2) != 0 ? -1 : 1), tip);
                buf.Set(px, py, White);
                light.Glow(ax, ay, 3 + arm * 2, p.Col, 0.5f * k * tw);
            }
        }
    }
}
