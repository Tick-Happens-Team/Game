// C# port of Sprite Fusion "slash" pixel VFX. MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt. Simulation units are the original's "screen" units (2 per art pixel).
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public sealed class SlashOptions
{
    /// <summary>Arc center in simulation units (art pixels * 2).</summary>
    public float X = 690, Y = 530;
    public float Size = 1;
    /// <summary>Arc radius in simulation units, before <see cref="Size"/>.</summary>
    public float Radius = 84;
    /// <summary>Vertical squash of the arc (1 = circle).</summary>
    public float Squash = 0.6f;
    /// <summary>Arc rotation, radians. Mirrored by <see cref="Facing"/>.</summary>
    public float Tilt = -0.38f;
    /// <summary>Start angle of the sweep, radians.</summary>
    public float From = -2.55f;
    /// <summary>End angle of the sweep, radians.</summary>
    public float To = 0.95f;
    /// <summary>Blade thickness in simulation units, before <see cref="Size"/>.</summary>
    public float Width = 24;
    /// <summary>1 sweeps one way, -1 mirrors the arc.</summary>
    public float Facing = 1;
    /// <summary>Seconds for the head to reach the end of the arc.</summary>
    public float Sweep = 0.06f;
    /// <summary>Seconds the full arc is held before the tail starts chasing the head.</summary>
    public float Hold = 0.06f;
    /// <summary>Seconds the tail takes to catch up with the head.</summary>
    public float Trail = 0.24f;
    /// <summary>Sparks thrown off the tip when the sweep lands.</summary>
    public float Sparks = 10;
    /// <summary>Seven-stop blade ramp (core to shadow). Null means the steel palette.</summary>
    public uint[]? Ramp;
    /// <summary>Four-stop spark ramp (hot to ember). Null means the hot palette.</summary>
    public uint[]? SparkRamp;
    /// <summary>Ground line for sparks. Top-down: defaults to Y (the arc center).</summary>
    public float? Ground;
    /// <summary>Per-x ground height; overrides <see cref="Ground"/> when set.</summary>
    public Func<float, float>? GroundAt;
    public uint Seed = 1;
    public Action<float>? Shake;
}

public sealed class SlashEffect : IPixelEffect
{
    internal const float PX = 2, Gravity = 1900;

    private static readonly uint White = Hex("#ffffff");

    private static readonly uint[] Steel =
        [Hex("#ffffff"), Hex("#e8f1ff"), Hex("#b8c9e8"), Hex("#62c8f4"), Hex("#3a8bd8"), Hex("#2c58aa"), Hex("#212f6b")];

    private static readonly uint[] Hot = [Hex("#fff3a6"), Hex("#ffe26b"), Hex("#ff8a2a"), Hex("#f2561d")];

    private readonly Slash _a, _e;
    private readonly Particles _ps;
    private readonly VfxRng _r;
    private readonly uint[] _ramp;
    private readonly float _s, _sparks, _endX, _endY, _endDx, _endDy;
    private readonly Action<float>? _shake;
    private float _t;
    private bool _landed;

    public SlashEffect(SlashOptions o)
    {
        float s = o.Size, f = o.Facing;
        _s = s;
        _sparks = o.Sparks;
        _shake = o.Shake;
        _ramp = o.Ramp is { Length: > 0 } custom ? custom : Steel;
        _r = new VfxRng(o.Seed);
        float gy = o.Ground ?? o.Y;
        Func<float, float> groundAt = o.GroundAt ?? (_ => gy);
        _ps = new Particles(groundAt, o.SparkRamp is { Length: > 0 } sr ? sr : Hot);

        float ra = o.Radius * s / PX, a0 = o.From, a1 = o.To;
        float Ang(float a) => f < 0 ? MathF.PI - a : a;

        _a = new Slash
        {
            Cx = o.X / PX, Cy = o.Y / PX, R = ra, Sy = o.Squash, Rot = o.Tilt * f,
            A0 = Ang(a0), A1 = Ang(a1), W = o.Width * s / PX, T0 = 0,
            Sweep = o.Sweep, Hold = o.Hold, Trail = o.Trail, Ramp = _ramp,
        };
        _e = _a.Copy();
        _e.R = ra * (33f / 42f);
        _e.W = ra * (4.5f / 42f);
        _e.A0 = Ang(a0 + (a1 - a0) * 0.1f);
        _e.A1 = Ang(a1 - (a1 - a0) * (0.15f / 3.5f));
        _e.T0 = 0.017f;
        _e.Hold = _a.Hold * 0.5f;
        _e.Trail = _a.Trail * (5f / 6f);
        _e.Ramp = _ramp.Length > 1 ? _ramp[1..] : _ramp;

        var end = SlashEnd(_a);
        _endX = end.X;
        _endY = end.Y;
        _endDx = end.Dx;
        _endDy = end.Dy;
    }

    public bool Done => _t > 0.5f && _ps.List.Count == 0;

    public void Update(float dt)
    {
        _t += dt;
        if (!_landed && _t >= _a.Sweep)
        {
            _landed = true;
            float dir = MathF.Atan2(_endDy, _endDx);
            for (int i = 0; i < _sparks; i++)
            {
                float a = dir + _r.Range(-0.5f, 0.5f), sp = _r.Range(300, 800);
                _ps.Spawn(Pt.Spark, _endX * PX, _endY * PX, MathF.Cos(a) * sp, MathF.Sin(a) * sp, _r.Range(0.12f, 0.35f));
            }
            Shavings(16);
            _shake?.Invoke(0.12f * _s);
        }
        _ps.Update(dt);
    }

    public void Draw(PixelFrame f, VfxLayer layer)
    {
        if (layer == VfxLayer.Back) return;
        var buf = PixelCanvas.ColorOf(f);
        var light = PixelCanvas.LightOf(f);
        DrawSlash(buf, light, _e, _t);
        DrawSlash(buf, light, _a, _t);
        Glint(buf, light, _a, _t, 9 * _s, 0.22f, _ramp);
        if (_t < 0.3f)
        {
            var (x, y) = OnArc(_a.Cx, _a.Cy, _a.R - 4, _a.Sy, _a.Rot, _a.A0 + (_a.A1 - _a.A0) * OutExpo(_t / _a.Sweep));
            light.Glow(x, y, 40 * _s, At(_ramp, 3), 0.5f * (1 - _t / 0.3f));
        }
        _ps.Draw(buf, light);
    }

    // ---- arc geometry ---------------------------------------------------------------------
    private sealed class Slash
    {
        public float Cx, Cy, R, Sy, Rot, A0, A1, W, T0, Sweep, Hold, Trail;
        public uint[] Ramp = [];

        public Slash Copy() => (Slash)MemberwiseClone();
    }

    private readonly struct EndPoint(float x, float y, float dx, float dy)
    {
        public readonly float X = x, Y = y, Dx = dx, Dy = dy;
    }

    private static (float X, float Y) OnArc(float cx, float cy, float r, float sy, float rot, float a)
    {
        float lx = MathF.Cos(a) * r, ly = MathF.Sin(a) * r * sy, c = MathF.Cos(rot), s = MathF.Sin(rot);
        return (cx + lx * c - ly * s, cy + lx * s + ly * c);
    }

    private static EndPoint SlashEnd(Slash s)
    {
        var (x, y) = OnArc(s.Cx, s.Cy, s.R - s.W * 0.3f, s.Sy, s.Rot, s.A1);
        var (x2, y2) = OnArc(s.Cx, s.Cy, s.R - s.W * 0.3f, s.Sy, s.Rot, s.A1 + MathF.Sign(s.A1 - s.A0) * 0.05f);
        float l = MathF.Sqrt((x2 - x) * (x2 - x) + (y2 - y) * (y2 - y));
        if (l == 0) l = 1;
        return new EndPoint(x, y, (x2 - x) / l, (y2 - y) / l);
    }

    // ---- blade ----------------------------------------------------------------------------
    private static void DrawSlash(PixelCanvas buf, PixelCanvas light, Slash s, float t)
    {
        float u = t - s.T0;
        if (u < 0) return;
        float head = OutExpo(u / s.Sweep), tail = Smooth(s.Sweep + s.Hold, s.Sweep + s.Hold + s.Trail, u), span = s.A1 - s.A0;
        float age = Clamp(u / (s.Sweep + s.Hold + s.Trail));
        if (tail < 1)
            Crescent(buf, light, s.Cx, s.Cy, s.R, s.Sy, s.Rot, s.A0 + span * tail, span * (head - tail), s.W * (1 - tail * 0.55f), s.Ramp, age);
        float th = 0.7f * (1 - Clamp((u - s.Sweep - s.Hold) / (s.Trail + 0.2f)));
        if (tail > 0.02f && th > 0)
            for (float k = 0; k <= tail; k += 0.6f / (MathF.Abs(span) * s.R))
            {
                float a = s.A0 + span * k;
                var (x, y) = OnArc(s.Cx, s.Cy, s.R - 0.5f, s.Sy, s.Rot, a);
                buf.Dot(x, y, At(s.Ramp, s.Ramp.Length - 2), th * (0.4f + 0.6f * k));
            }
    }

    private static void Crescent(PixelCanvas buf, PixelCanvas light, float cx, float cy, float r0, float sy, float rot,
        float ta, float len, float w0, uint[] ramp, float age)
    {
        if (MathF.Abs(len) < 0.02f || w0 < 0.3f) return;
        float dir = MathF.Sign(len), span = MathF.Abs(len), c = MathF.Cos(rot), s = MathF.Sin(rot);
        int n = ramp.Length, b = (int)MathF.Ceiling(r0 + 2);
        for (int py = (int)MathF.Floor(cy - b); py <= (int)MathF.Ceiling(cy + b); py++)
            for (int px = (int)MathF.Floor(cx - b); px <= (int)MathF.Ceiling(cx + b); px++)
            {
                float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                float lx = dx * c + dy * s, ly = (-dx * s + dy * c) / sy, r = MathF.Sqrt(lx * lx + ly * ly);
                if (r > r0 + 0.5f || r < r0 - w0 - 1) continue;
                float u = (dir * (MathF.Atan2(ly, lx) - ta)) % Tau;
                if (u < 0) u += Tau;
                if (u > span) continue;
                float v = u / span, w = w0 * MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(v, 2.2f)), 0.6f);
                if (w < 0.35f) continue;
                float q = (r0 - r) / w;
                if (q < 0 || q > 1) continue;
                float f = MathF.Min(1, v / 0.4f);
                if (f < 1 && f <= Bayer(px, py)) continue;
                int ci = q < 0.22f && age < 0.5f ? 0 : Round(q * (n - 1) * 0.75f + age * 2.4f - v * 0.8f + 0.4f);
                buf.Set(px, py, At(ramp, ci));
            }
        for (float u = 0; u <= span; u += 0.3f)
        {
            var (x, y) = OnArc(cx, cy, r0 - w0 * 0.4f, sy, rot, ta + dir * u);
            light.Glow(x, y, 5 + w0, At(ramp, 2), 0.22f * (1 - age) * (u / span));
        }
    }

    // ---- tip glint ------------------------------------------------------------------------
    private static void Glint(PixelCanvas buf, PixelCanvas light, Slash s, float t, float arm, float life, uint[] c)
    {
        float g = t - s.T0 - s.Sweep * 0.6f;
        if (g <= 0 || g >= life) return;
        var e = SlashEnd(s);
        Sparkle(buf, light, e.X, e.Y, arm * OutBack(MathF.Min(1, g / 0.05f)) * (1 - g / life), At(c, 1), At(c, 3));
    }

    private static void Sparkle(PixelCanvas buf, PixelCanvas light, float x, float y, float arm, uint c, uint tip)
    {
        int px = Round(x), py = Round(y), n = Round(arm);
        if (n < 1) return;
        for (int d = 1; d <= n; d++)
        {
            uint cc = d > n * 0.6f ? tip : c;
            buf.Set(px + d, py, cc);
            buf.Set(px - d, py, cc);
            buf.Set(px, py + d, cc);
            buf.Set(px, py - d, cc);
        }
        if (n >= 3)
            foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                buf.Set(px + dx, py + dy, c);
        if (n >= 6)
            foreach (var (dx, dy) in new[] { (2, 2), (2, -2), (-2, 2), (-2, -2) })
                buf.Set(px + dx, py + dy, tip);
        buf.Set(px, py, White);
        light.Glow(x, y, 4 + n * 2, c, 0.6f);
    }

    // ---- landing shavings -----------------------------------------------------------------
    private void Shavings(int n)
    {
        for (int i = 0; i < n; i++)
        {
            float a = _a.A0 + (_a.A1 - _a.A0) * _r.Range(0.3f, 1);
            var (x, y) = OnArc(_a.Cx, _a.Cy, _a.R, _a.Sy, _a.Rot, a);
            float ox = x - _a.Cx, oy = y - _a.Cy, l = MathF.Sqrt(ox * ox + oy * oy);
            if (l == 0) l = 1;
            float sp = _r.Range(60, 200);
            _ps.Spawn(Pt.Mote, x * PX, y * PX, ox / l * sp, oy / l * sp - 20, _r.Range(0.25f, 0.55f), At(_ramp, 2 + _r.Int(0, 1)), 0.6f);
        }
    }

    // ---- helpers --------------------------------------------------------------------------
    /// <summary>JS Math.round: halves round up, unlike MathF.Round's banker's rounding.</summary>
    private static int Round(float v) => (int)MathF.Floor(v + 0.5f);

    private static uint At(uint[] r, int i) => r[Math.Clamp(i, 0, r.Length - 1)];

    private static float Smooth(float a, float b, float x)
    {
        float t = Clamp((x - a) / (b - a));
        return t * t * (3 - 2 * t);
    }

    private static float OutExpo(float t) => t >= 1 ? 1 : 1 - MathF.Pow(2, -10 * Clamp(t));

    private static float OutBack(float t, float s = 1.7f)
    {
        float u = Clamp(t) - 1;
        return 1 + (s + 1) * u * u * u + s * u * u;
    }

    // ---- particles ------------------------------------------------------------------------
    private enum Pt : byte { Spark, Mote }

    private sealed class Part
    {
        public Pt K;
        public float X, Y, Vx, Vy, Life, Max, Size;
        public uint Col;
    }

    private sealed class Particles(Func<float, float> ground, uint[] sparks)
    {
        public readonly List<Part> List = [];

        public void Spawn(Pt k, float x, float y, float vx, float vy, float life, uint col = 0, float size = 1) =>
            List.Add(new Part { K = k, X = x, Y = y, Vx = vx, Vy = vy, Life = life, Max = life, Col = col, Size = size });

        public void Update(float dt)
        {
            var L = List;
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var p = L[i];
                if ((p.Life -= dt) <= 0) { L.RemoveAt(i); continue; }
                if (p.K == Pt.Spark)
                {
                    p.Vy += Gravity * 0.45f * dt;
                    p.Vx -= p.Vx * 2.2f * dt;
                    p.Vy -= p.Vy * 2.2f * dt;
                    float g = ground(p.X), ny = p.Y + p.Vy * dt;
                    p.X += p.Vx * dt;
                    if (p.Vy > 0 && p.Y <= g && ny >= g) { p.Vy = -p.Vy * 0.45f; p.Vx *= 0.7f; p.Y = g - 0.5f; }
                    else p.Y = ny;
                }
                else
                {
                    p.Vx -= p.Vx * 0.6f * dt;
                    p.Vy -= p.Vy * 0.6f * dt;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                }
            }
        }

        public void Draw(PixelCanvas buf, PixelCanvas light)
        {
            foreach (var p in List)
            {
                float k = p.Life / p.Max, ax = p.X / PX, ay = p.Y / PX;
                float sp = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                if (sp == 0) sp = 1;
                float ux = p.Vx / sp, uy = p.Vy / sp;
                if (p.K == Pt.Spark)
                {
                    uint head = k > 0.6f ? Spark(0) : k > 0.3f ? Spark(1) : Spark(2);
                    float len = MathF.Min(9f, 1 + sp * 0.011f);
                    buf.Line(ax - ux * len, ay - uy * len, ax - ux * len * 0.4f, ay - uy * len * 0.4f, k > 0.5f ? head : Spark(sparks.Length - 1));
                    buf.Line(ax - ux * len * 0.45f, ay - uy * len * 0.45f, ax, ay, head);
                    light.Glow(ax, ay, 3, head, 0.45f * k);
                }
                else
                {
                    int px = (int)MathF.Floor(ax), py = (int)MathF.Floor(ay);
                    float len = MathF.Min(16f, 0.5f + sp * 0.013f) * p.Size;
                    if (k < 0.35f && Bayer(px, py) > k / 0.35f) continue;
                    buf.Line(ax - ux * len, ay - uy * len, ax - ux * len * 0.35f, ay - uy * len * 0.35f, Scale(p.Col, 0.55f));
                    buf.Line(ax - ux * len * 0.4f, ay - uy * len * 0.4f, ax, ay, p.Col);
                    light.Glow(ax, ay, 2.5f + p.Size, p.Col, 0.4f * k);
                }
            }
        }

        private uint Spark(int i) => sparks[Math.Clamp(i, 0, sparks.Length - 1)];
    }
}
