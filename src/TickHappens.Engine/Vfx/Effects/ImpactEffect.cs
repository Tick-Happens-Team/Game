// C# port of Sprite Fusion "impact" pixel VFX. MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt. Simulation units are the original's "screen" units (2 per art pixel).
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public enum ImpactKind
{
    /// <summary>Head-on hit: compact star burst.</summary>
    Hit,
    /// <summary>Glancing ricochet: stretched star, streak and surface scorch.</summary>
    Ricochet,
}

public sealed class ImpactOptions
{
    public ImpactKind Kind = ImpactKind.Hit;
    /// <summary>Impact point in simulation units (art pixels * 2).</summary>
    public float X = 760, Y = 600;
    /// <summary>Incoming direction, radians.</summary>
    public float Angle = 0.2f;
    /// <summary>Surface normal, radians. Only used when <see cref="Surface"/> is set.</summary>
    public float Normal = -MathF.PI / 2;
    /// <summary>Scale. Unset means 1 for a hit and 0.9 for a ricochet.</summary>
    public float? Size;
    /// <summary>Six-stop color ramp (core, hot, yellow, orange, deep orange, red).</summary>
    public uint[]? Ramp;
    /// <summary>Surface color the impact chips off. Null means the hit happens in open air.</summary>
    public uint? Surface;
    /// <summary>Ground line for debris. Top-down: defaults to Y (the impact point).</summary>
    public float? Ground;
    /// <summary>Per-x ground height; overrides <see cref="Ground"/> when set.</summary>
    public Func<float, float>? GroundAt;
    public uint Seed = 1;
    public Action<float>? Shake;
}

public sealed class ImpactEffect : IPixelEffect
{
    internal const float PX = 2, Gravity = 1900, Streak = 0.16f, Speed = 1500;

    private static readonly uint Soot = Hex("#0d0a12"), Smoke = Hex("#6a6278");

    private static readonly uint[] WarmRamp =
        [Hex("#ffffff"), Hex("#fff3a6"), Hex("#ffe26b"), Hex("#ff8a2a"), Hex("#f2561d"), Hex("#c9281f")];

    private static readonly uint[] SmokeRamp =
        [Hex("#7b6f72"), Hex("#5d5660"), Hex("#433d48"), Hex("#2b2630"), Hex("#1a171e")];

    private readonly Particles _ps;
    private readonly uint[] _ramp;
    private readonly Spike[] _spikes;
    private readonly Shard[] _shards;
    private readonly bool _glance, _onSurface;
    private readonly float _S, _ax, _ay, _N, _tx, _ty, _sx, _sy;
    private float _t;

    public ImpactEffect(ImpactOptions o)
    {
        bool glance = o.Kind == ImpactKind.Ricochet;
        _glance = glance;
        float X = o.X, Y = o.Y, A = o.Angle, S = o.Size ?? (glance ? 0.9f : 1);
        _S = S;
        _ramp = o.Ramp is { Length: > 0 } custom ? custom : WarmRamp;
        var R = new VfxRng(o.Seed);
        float gy = o.Ground ?? Y;
        Func<float, float> groundAt = o.GroundAt ?? (_ => gy);
        _ps = new Particles(R, groundAt, _ramp);
        _ax = X / PX;
        _ay = Y / PX;
        float dx = MathF.Cos(A), dy = MathF.Sin(A);
        _onSurface = o.Surface is not null;
        float N = o.Normal;
        _N = N;
        float nx = MathF.Cos(N), ny = MathF.Sin(N), dn = dx * nx + dy * ny;
        float B = _onSurface ? MathF.Atan2(dy - 2 * dn * ny, dx - 2 * dn * nx) : A;

        _spikes = new Spike[8];
        for (int i = 0; i < 8; i++)
        {
            float a = B + (float)i / 8 * Tau + R.Range(-0.18f, 0.18f), cb = MathF.Cos(a - B);
            bool into = _onSurface && MathF.Cos(a - N) < -0.2f;
            float len = (7 + R.Range(0, 3)) * S
                * (1 + (glance ? 2.6f : 1.8f) * MathF.Pow(MathF.Max(0, cb), 3) + 0.6f * MathF.Pow(MathF.Max(0, -cb), 3))
                * (into ? 0.3f : 1)
                * (glance && MathF.Abs(MathF.Sin(a - B)) > 0.6f ? 0.6f : 1);
            _spikes[i] = new Spike { A = a, Len = len, W = (1.5f + R.Range(0, 0.6f)) * S };
        }

        var picked = _spikes.Where(p => Open(p.A)).OrderByDescending(p => p.Len).Take(4).ToList();
        _shards = new Shard[picked.Count];
        for (int i = 0; i < picked.Count; i++)
        {
            var s = picked[i];
            _shards[i] = new Shard { A = s.A + R.Range(-0.15f, 0.15f), R = s.Len * 0.8f, V = s.Len * R.Range(5, 8), Arc = R.Range(0.35f, 0.6f) };
        }

        int n = Round((glance ? 10 : 6) + 12 * S);
        for (int i = 0; i < n; i++)
        {
            bool back = !_onSurface && R.Chance(0.2f);
            float a = (back ? A + MathF.PI : B) + R.Range(-1, 1) * (glance ? 0.3f : _onSurface ? 0.85f : 0.6f);
            float sp = R.Range(260, 900) * (0.6f + 0.4f * S) * (glance ? 1.2f : 1);
            _ps.Spawn(Pt.Spark, X, Y, MathF.Cos(a) * sp, MathF.Sin(a) * sp - (glance ? 20 : 60), R.Range(0.1f, 0.3f) * (glance ? 1.3f : 1));
        }

        if (_onSurface)
        {
            uint sc = o.Surface!.Value, dc = Mix(sc, Rgb(150, 140, 150), 0.35f);
            int chips = Round((glance ? 2 : 3) + 5 * S);
            for (int i = 0; i < chips; i++)
            {
                float a = (glance ? B : N) + R.Range(-0.8f, 0.8f) * (glance ? 0.6f : 1), sp = R.Range(150, 420) * S;
                _ps.Spawn(Pt.Chip, X, Y + ny * 2, MathF.Cos(a) * sp, MathF.Sin(a) * sp, R.Range(0.8f, 1.4f),
                    R.Chance(0.5f) ? Scale(sc, 1.25f) : R.Chance(0.5f) ? sc : Scale(sc, 0.7f), R.Chance(0.3f) ? 2 : 1);
            }
            int dusts = Round(3 + 4 * S);
            for (int i = 0; i < dusts; i++)
            {
                float a = N + R.Range(-1, 1), sp = R.Range(20, 90) * S;
                _ps.Spawn(Pt.Dust, X + nx * 3, Y + ny * 3, MathF.Cos(a) * sp, MathF.Sin(a) * sp, R.Range(0.35f, 0.7f), dc, R.Range(1.4f, 2.8f) * MathF.Sqrt(S));
            }
            _ps.Spawn(Pt.Smoke, X + nx * 4, Y + ny * 4, R.Range(-8, 8), R.Range(-30, -14), R.Range(0.8f, 1.3f), Smoke,
                R.Range(1.6f, 2.4f) * MathF.Sqrt(S), 0.05f);
        }

        o.Shake?.Invoke(0.03f * S);

        _tx = MathF.Cos(B);
        _ty = MathF.Sin(B);
        float side = N + MathF.PI / 2, cs = MathF.Cos(A - side);
        float sgn = MathF.Sign(cs == 0 ? 1 : cs);
        _sx = MathF.Cos(side) * sgn;
        _sy = MathF.Sin(side) * sgn;
    }

    public bool Done => _t > (_glance ? 1.1f : 0.2f) && _ps.List.Count == 0;

    public void Update(float dt)
    {
        _t += dt;
        _ps.Update(dt);
    }

    public void Draw(PixelFrame f, VfxLayer layer)
    {
        var buf = PixelCanvas.ColorOf(f);
        var light = PixelCanvas.LightOf(f);
        if (layer != VfxLayer.Front)
        {
            if (_glance && _onSurface && _t < 1.1f)
            {
                float T = Clamp(1 - _t / 0.9f), L = 9 * _S;
                for (float d = 0; d <= L; d += 0.5f)
                {
                    float k = d / L, px = _ax - _sx * d, py = _ay - _sy * d;
                    uint c = T * (1 - k) > 0.5f ? Col(1) : T * (1 - k) > 0.25f ? Col(3) : T > 0 ? Col(5) : Soot;
                    buf.Dot(px, py, c, 1 - k * 0.6f);
                    if (T > 0.2f && (int)d % 3 == 0) light.Glow(px, py, 4, Col(3), 0.3f * T * (1 - k));
                }
            }
            _ps.Draw(buf, light, back: true);
        }
        if (layer == VfxLayer.Back) return;

        int fr = Math.Max(1, (int)MathF.Ceiling(_t * 60 - 1e-6f));
        if (fr == 1) Star(buf, _ax, _ay, _spikes, _S, 1, 1, Col(0), Col(1), Col(2), Col(4));
        else if (fr == 2) Star(buf, _ax, _ay, _spikes, _S, 1.25f, 0.55f, Col(1), Col(2), Col(3), Col(4));

        if (fr >= 3 && _t < 0.13f)
        {
            float k = (_t - 2f / 60) / (0.13f - 2f / 60);
            foreach (var s in _shards)
                Crescent(buf, _ax, _ay, s.A, s.R + s.V * (_t - 2f / 60), s.Arc * (1 - 0.4f * k),
                    k < 0.4f ? Col(2) : k < 0.7f ? Col(3) : Col(4), 1 - k);
        }

        if (fr >= 3 && _t < 0.2f)
        {
            float k = (_t - 2f / 60) / (0.2f - 2f / 60), r = (8 + 12 * (1 - MathF.Pow(1 - k, 3))) * _S;
            float th = MathF.Max(1, 2 * (1 - k) * _S);
            uint col = k < 0.3f ? Col(1) : k < 0.6f ? Col(2) : Col(3);
            int n = (int)MathF.Ceiling(r * Tau * 1.3f);
            for (int j = 0; j < n; j++)
            {
                float a = (float)j / n * Tau;
                if (!Open(a)) continue;
                for (float off = -th / 2; off <= th / 2; off += 0.5f)
                    buf.Dot(_ax + MathF.Cos(a) * (r + off), _ay + MathF.Sin(a) * (r + off), col, 1.15f - k);
            }
        }

        if (_glance && _t < Streak)
        {
            float head = Speed * _t / PX + 6 * _S, tail = MathF.Max(2, head - (40 + 220 * _t) / PX), k = _t / Streak;
            for (float d = tail; d <= head; d += 0.5f)
            {
                float u = (d - tail) / MathF.Max(1, head - tail);
                buf.Dot(_ax + _tx * d, _ay + _ty * d, u > 0.8f ? Col(0) : u > 0.4f ? Col(1) : Col(3), u > 0.4f ? 1 : 1 - k);
            }
            light.Glow(_ax + _tx * head, _ay + _ty * head, 9, Col(2), 0.6f * (1 - k));
        }

        _ps.Draw(buf, light, back: false);

        float pop = MathF.Exp(-_t / 0.05f);
        if (pop > 0.02f)
        {
            light.Glow(_ax, _ay, 22 * _S + 6, Col(3), 0.9f * pop);
            light.Glow(_ax, _ay, 8 * _S + 2, Col(0), 0.7f * pop);
        }
    }

    private uint Col(int i) => _ramp[Math.Min(_ramp.Length - 1, i)];

    /// <summary>JS Math.round: halves round up, unlike MathF.Round's banker's rounding.</summary>
    private static int Round(float v) => (int)MathF.Floor(v + 0.5f);

    private bool Open(float a) => !_onSurface || MathF.Cos(a - _N) > -0.25f;

    // ---- star, shards ---------------------------------------------------------------------
    private sealed class Spike { public float A, Len, W; }

    private sealed class Shard { public float A, R, V, Arc; }

    private static void Star(PixelCanvas buf, float ax, float ay, Spike[] spikes, float S, float sc, float th,
        uint core, uint body, uint inner, uint rim)
    {
        for (int pass = 0; pass < 3; pass++)
            foreach (var s in spikes)
            {
                float c = MathF.Cos(s.A), sn = MathF.Sin(s.A), L = s.Len * sc;
                for (float d = 0; d <= L; d += 0.5f)
                {
                    float hw0 = s.W * th * (1 - d / L) * MathF.Min(1, 0.5f + d / 3);
                    float hw = pass == 0 ? hw0 + 0.8f : pass == 1 ? hw0 : hw0 * 0.45f;
                    if (pass == 2 && d > L * 0.55f) break;
                    for (float o = -hw; o <= hw; o += 0.5f)
                        buf.Set(MathF.Floor(ax + c * d - sn * o), MathF.Floor(ay + sn * d + c * o),
                            pass == 0 ? rim : pass == 1 ? (d < L * 0.35f ? inner : body) : core);
                }
            }
        float r = 2.4f * S * sc * MathF.Sqrt(th);
        buf.Disc(ax, ay, r + 0.8f, inner);
        buf.Disc(ax, ay, r, core);
    }

    private static void Crescent(PixelCanvas buf, float ax, float ay, float a, float r, float arc, uint c, float k)
    {
        int n = (int)MathF.Ceiling(r * arc * 2.2f);
        if (n <= 0) return;
        for (int j = 0; j <= n; j++)
        {
            float u = (float)j / n - 0.5f, th = a + u * arc, w = 1.4f * (1 - MathF.Abs(u) * 2);
            for (float o = -w * 0.5f; o <= w * 0.5f + 0.01f; o += 0.5f)
                buf.Dot(ax + MathF.Cos(th) * (r + o), ay + MathF.Sin(th) * (r + o), c, k);
        }
    }

    // ---- particles ------------------------------------------------------------------------
    private enum Pt : byte { Spark, Chip, Dust, Smoke }

    private sealed class Part
    {
        public Pt K;
        public float X, Y, Vx, Vy, Life, Max, Size, Seed, Delay;
        public uint Col;
    }

    private sealed class Particles(VfxRng r, Func<float, float> ground, uint[] ramp)
    {
        public readonly List<Part> List = [];
        public readonly VfxRng R = r;
        public float T;

        private uint Col(int i) => ramp[Math.Min(ramp.Length - 1, i)];

        public void Spawn(Pt k, float x, float y, float vx, float vy, float life, uint col = 0, float size = 1, float delay = 0) =>
            List.Add(new Part
            {
                K = k, X = x, Y = y, Vx = vx, Vy = vy, Life = life, Max = life, Col = col, Size = size,
                Seed = R.Next() * 100, Delay = delay,
            });

        public void Update(float dt)
        {
            T += dt;
            float t = T;
            var L = List;
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var p = L[i];
                if (p.Delay > 0) { p.Delay -= dt; continue; }
                if ((p.Life -= dt) <= 0) { L.RemoveAt(i); continue; }
                float g = ground(p.X);
                switch (p.K)
                {
                    case Pt.Spark:
                    {
                        p.Vy += Gravity * 0.45f * dt;
                        p.Vx -= p.Vx * 2.2f * dt;
                        p.Vy -= p.Vy * 2.2f * dt;
                        float ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g) { p.Vy = -p.Vy * 0.45f; p.Vx *= 0.7f; p.Y = g - 0.5f; }
                        else p.Y = ny;
                        break;
                    }
                    case Pt.Chip:
                    {
                        p.Vy += Gravity * dt;
                        p.Vx -= p.Vx * 0.6f * dt;
                        float ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g)
                        {
                            p.Y = g - 0.5f;
                            if (p.Vy > 220) { p.Vy = -p.Vy * 0.3f; p.Vx = p.Vx * 0.5f + R.Range(-20, 20); }
                            else { p.Vx = p.Vy = 0; p.Life = MathF.Min(p.Life, 0.5f); }
                        }
                        else p.Y = ny;
                        break;
                    }
                    case Pt.Dust:
                        p.Vx -= p.Vx * 4 * dt;
                        p.Vy -= p.Vy * 4 * dt + 10 * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                    case Pt.Smoke:
                        p.Vy -= 32 * dt;
                        p.Vx += MathF.Sin(t * 1.3f + p.Seed) * 16 * dt;
                        p.Vx -= p.Vx * 1.6f * dt;
                        p.Vy -= p.Vy * 1.2f * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                }
            }
        }

        public void Draw(PixelCanvas buf, PixelCanvas light, bool back)
        {
            foreach (var p in List)
            {
                if (p.Delay > 0 || (p.K == Pt.Spark) == back) continue;
                float k = p.Life / p.Max, ax = p.X / PX, ay = p.Y / PX;
                int px = (int)MathF.Floor(ax), py = (int)MathF.Floor(ay);
                switch (p.K)
                {
                    case Pt.Spark:
                    {
                        uint hot = k > 0.6f ? Col(1) : k > 0.3f ? Col(2) : Col(3);
                        float sp = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                        if (sp == 0) sp = 1;
                        float ux = p.Vx / sp, uy = p.Vy / sp, len = MathF.Min(9, 1 + sp * 0.011f);
                        buf.Line(ax - ux * len, ay - uy * len, ax - ux * len * 0.4f, ay - uy * len * 0.4f, k > 0.5f ? hot : Col(4));
                        buf.Line(ax - ux * len * 0.45f, ay - uy * len * 0.45f, ax, ay, hot);
                        light.Glow(ax, ay, 3, hot, 0.45f * k);
                        break;
                    }
                    case Pt.Chip:
                        if (k > 0.25f || Bayer(px, py) < k * 4)
                        {
                            buf.Set(px, py, p.Col);
                            if (p.Size >= 2)
                            {
                                buf.Set(px + 1, py, p.Col);
                                buf.Set(px, py + 1, Scale(p.Col, 0.8f));
                            }
                        }
                        break;
                    case Pt.Dust:
                        buf.AlphaDisc(ax, ay, p.Size * (0.6f + (1 - k) * 0.9f), p.Col, MathF.Pow(k, 1.3f) * 0.7f);
                        break;
                    case Pt.Smoke:
                    {
                        float age = 1 - k, r = p.Size * (0.65f + MathF.Min(1, age * 2.5f) * 0.45f + age * 0.4f);
                        float a = MathF.Min(1, age * 12) * MathF.Min(1, k * 2.4f) * 0.5f;
                        uint c = Mix(p.Col, Ramp(SmokeRamp, age), age);
                        buf.AlphaDisc(ax, ay, r, c, a * 0.45f);
                        buf.AlphaDisc(ax + r * 0.05f, ay + r * 0.06f, r * 0.74f, c, a * 0.62f);
                        break;
                    }
                }
            }
        }
    }
}
