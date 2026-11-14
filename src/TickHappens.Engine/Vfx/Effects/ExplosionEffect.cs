// C# port of Sprite Fusion "explosion" pixel VFX. MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt. Simulation units are the original's "screen" units (2 per art pixel).
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public sealed class ExplosionOptions
{
    /// <summary>Center in simulation units (art pixels * 2).</summary>
    public float X = 770, Y = 600;
    /// <summary>Ground line for debris/grit. Top-down: defaults to Y (the impact point).</summary>
    public float? Ground;
    public float Size = 1;
    public uint? Surface;
    public float Fire = 0.3f, Earth = 1.6f, Chunks = 26, Scorch = 22, Shockwave = 80;
    public uint Seed = 1;
    public Action<float>? Shake;
}

public sealed class ExplosionEffect : IPixelEffect
{
    internal const float PX = 2, Gravity = 1900;

    private readonly Particles _ps;
    private readonly Fx _fx;
    private readonly Bits _rocks;
    private readonly Stone _st;
    private readonly float _gx, _gy, _cool, _thick, _ring, _scorch;
    private readonly uint[] _ringCols;
    private float _t;

    public ExplosionEffect(ExplosionOptions o)
    {
        float s = o.Size, rad = 36 * s, power = 0.9f * MathF.Pow(s, 0.8f);
        _ring = o.Shockwave * s;
        _scorch = o.Scorch * s;
        var R = new VfxRng(o.Seed);
        float g = o.Ground ?? o.Y, X = o.X, Y = o.Y;
        Func<float, float> groundAt = _ => g;
        _ps = new Particles(R, groundAt);
        _fx = new Fx(_ps);
        _rocks = new Bits(groundAt);
        _st = new Stone(o.Surface ?? StoneBody);
        _gx = X / PX;
        _gy = g / PX;
        _cool = 1.2f + rad / 28;
        _thick = 2 + rad / 18;
        _ringCols = [White, Pal.Hot, Pal.Orange, _st.Light, _st.Body];

        _fx.Explosion(X, MathF.Min(Y, g - rad * 0.3f), rad, power, o.Fire);
        o.Shake?.Invoke(0.6f * MathF.Min(1.3f, MathF.Sqrt(s)));
        for (int i = 0; i < (int)MathF.Round(rad * 0.5f); i++)
        {
            float sg = R.Sign();
            _ps.Spawn(Pt.Smoke, X + sg * R.Range(0, rad * 0.6f), g - R.Range(2, 10 + rad * 0.08f), sg * R.Range(3, 9.5f) * rad, -R.Range(4, 30),
                R.Range(1.1f, 2.2f) + rad / 80, _st.Dust, R.Range(4, 7) + rad / 12, R.Range(0, 0.06f), Flags.Big);
        }
        for (int i = 0; i < o.Chunks * MathF.Min(1.5f, s); i++)
            _rocks.Add(X + R.Range(-0.4f, 0.4f) * rad, g - 6, R.Range(-9, 9) * rad * 0.4f, -R.Range(7, 16) * rad * 1.5f, R.Range(1.8f, 2.6f) + rad / 60,
                R.Range(0, Tau), R.Range(-16, 16), R.Int(1, 3));
        for (int i = 0; i < (int)MathF.Round(o.Earth * rad * 2.6f); i++)
        {
            float u = R.Range(-1, 1), a = -MathF.PI / 2 + u * 0.5f, sp = R.Range(6, 19) * rad * (1 - 0.45f * MathF.Abs(u));
            _ps.Spawn(Pt.Grit, X + u * 0.25f * rad, g - 2, MathF.Cos(a) * sp, MathF.Sin(a) * sp, R.Range(1.2f, 2.2f),
                R.Pick([_st.Light, _st.Body, _st.Body, _st.Shade]), R.Chance(0.45f) ? 2 : 1);
        }
        for (int i = 0; i < (int)MathF.Round(o.Earth * 12); i++)
            _ps.Spawn(Pt.Smoke, X + R.Range(-0.3f, 0.3f) * rad, g - R.Range(4, rad * 0.6f), R.Range(-30, 30), -R.Range(120, 380) * MathF.Sqrt(s) * o.Earth * 0.6f,
                R.Range(1.6f, 2.8f), _st.Dust, R.Range(5, 9) * MathF.Sqrt(s) + rad / 14, R.Range(0, 0.15f), Flags.Big);
    }

    public bool Done => _t > 1 && _ps.List.Count == 0 && !_fx.Busy && _rocks.List.Count == 0;

    public void Update(float dt)
    {
        _t += dt;
        _rocks.Step(dt, (b, sp) => { if (sp > 200) _fx.Dust(b.X, b.Y, 3, 0.6f, _st.Body); });
        _fx.Update(dt);
        _ps.Update(dt);
    }

    public void Draw(PixelFrame f, VfxLayer layer)
    {
        var buf = PixelCanvas.ColorOf(f);
        var light = PixelCanvas.LightOf(f);
        if (layer != VfxLayer.Front)
        {
            DrawScorch(buf, light, _gx, _gy, _t, _scorch, _cool);
            _ps.Draw(buf, light, back: true);
            _fx.DrawBack(buf, light);
            FloorRing(buf, _gx, _gy, _t / 0.5f, _ring, _thick, _ringCols, true);
        }
        if (layer != VfxLayer.Back)
        {
            FloorRing(buf, _gx, _gy, _t / 0.5f, _ring, _thick, _ringCols, false);
            foreach (var b in _rocks.List) DrawRock(buf, b, _st);
            _ps.Draw(buf, light, back: false);
            _fx.DrawFront(buf, light);
        }
    }

    // ---- palette -------------------------------------------------------------------------
    internal static readonly uint White = Hex("#ffffff"), Outline = Hex("#1a1422"), Soot = Hex("#0d0a12"), Soot2 = Hex("#17121d"), Warm = Rgb(255, 176, 96);
    private static readonly uint StoneBody = Hex("#4a3f69");

    internal static class Pal
    {
        public static readonly uint Hot = Hex("#fff3a6"), Yellow = Hex("#ffe26b"), Gold = Hex("#ffc23d"), Orange = Hex("#ff8a2a"),
            DeepOrange = Hex("#f2561d"), Red = Hex("#c9281f");
    }

    internal static readonly uint[] FireRamp =
        [White, Pal.Hot, Pal.Yellow, Pal.Gold, Pal.Orange, Pal.DeepOrange, Pal.Red, Hex("#8a1f17"), Hex("#4a1712")];
    private static readonly uint[] HeatRamp = [.. FireRamp.Take(8), Hex("#4f1a13"), Hex("#2a120e")];
    internal static readonly uint[] SmokeRamp = [Hex("#7b6f72"), Hex("#5d5660"), Hex("#433d48"), Hex("#2b2630"), Hex("#1a171e")];

    private readonly struct Stone(uint c)
    {
        public readonly uint Light = Mix(Scale(c, 1.35f), White, 0.08f);
        public readonly uint Body = c;
        public readonly uint Shade = Scale(c, 0.64f);
        public readonly uint Dust = Mix(Scale(c, 1.2f), Rgb(150, 140, 150), 0.3f);
    }

    // ---- ground marks --------------------------------------------------------------------
    private static void DrawScorch(PixelCanvas buf, PixelCanvas light, float cx, float cy, float t, float rx0, float cool)
    {
        float rx = rx0 * MathF.Min(1, t / 0.08f), ry = 4;
        for (int y = (int)MathF.Floor(cy); y <= cy + 2; y++)
            for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 1) continue;
                float k = Hash(x * 31 + y);
                float T = MathF.Pow(Clamp(1 - t / cool), 1.4f) * (1.25f - d) - k * 0.35f;
                if (T > 0.06f)
                {
                    uint c = HeatRamp[Math.Min(HeatRamp.Length - 1, (int)MathF.Floor((1 - MathF.Min(1, T)) * (HeatRamp.Length - 1)))];
                    buf.Set(x, y, c);
                    if ((x & 3) == 0) light.Glow(x + 0.5f, y, 6, c, 0.18f * T);
                }
                else if (d < 0.95f - k * 0.3f || Bayer(x, y) < 1 - d) buf.Set(x, y, d < 0.5f ? Soot : Soot2);
            }
    }

    private static void FloorRing(PixelCanvas buf, float x, float gy, float k, float rx, float thick, uint[] cols, bool back)
    {
        if (k >= 1 || k < 0) return;
        float r = rx * OutCubic(k);
        uint col = cols[Math.Min(cols.Length - 1, (int)MathF.Floor(k * cols.Length))];
        RingE(buf, x, gy + 0.5f, r, r * 0.16f, MathF.Max(1, thick * (1 - k)), col, 1.15f - k, a => back ? MathF.Sin(a) < 0 : MathF.Sin(a) >= 0);
    }

    private static void DrawRock(PixelCanvas buf, Bit b, Stone st)
    {
        float x = b.X / PX, y = b.Y / PX, r = 0.6f + b.S * 0.75f, fade = MathF.Min(1, b.Life / 0.5f);
        float lx = MathF.Cos(b.A) * r * 0.4f, ly = MathF.Sin(b.A) * r * 0.4f;
        for (int py = (int)MathF.Floor(y - r - 1); py <= (int)MathF.Ceiling(y + r + 1); py++)
            for (int px = (int)MathF.Floor(x - r - 1); px <= (int)MathF.Ceiling(x + r + 1); px++)
            {
                float dx = px + 0.5f - x, dy = py + 0.5f - y, d = MathF.Sqrt(dx * 1.15f * dx * 1.15f + dy * dy);
                if (d > r + 0.9f || (fade < 1 && fade <= Bayer(px, py))) continue;
                float lit = dx * lx + dy * ly;
                buf.Set(px, py, d > r ? Outline : lit < -0.4f ? st.Light : lit > 0.6f ? st.Shade : st.Body);
            }
    }

    // ---- particles -----------------------------------------------------------------------
    internal enum Pt : byte { Spark, Ember, Smoke, Fire, Brand, Dust, Grit }

    [Flags]
    internal enum Flags : byte { None = 0, Smokes = 1, Big = 2, Back = 4 }

    internal sealed class Part
    {
        public Pt K;
        public float X, Y, Vx, Vy, Life, Max, Size, Seed, Delay;
        public uint Col;
        public Flags Flag;
    }

    internal sealed class Particles(VfxRng r, Func<float, float> ground)
    {
        public readonly List<Part> List = [];
        public readonly VfxRng R = r;
        public float T;
        public Action<float, float>? OnLand;

        public void Spawn(Pt k, float x, float y, float vx, float vy, float life, uint col = 0, float size = 1, float delay = 0, Flags flag = Flags.None) =>
            List.Add(new Part { K = k, X = x, Y = y, Vx = vx, Vy = vy, Life = life, Max = life, Col = col, Size = size, Seed = R.Next() * 100, Delay = delay, Flag = flag });

        public void Impulse(float x, float y, float r, float force)
        {
            foreach (var p in List)
            {
                if (p.K == Pt.Fire) continue;
                float dx = p.X - x, dy = p.Y - y, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d == 0) d = 1;
                if (d > r) continue;
                float f = force * (1 - d / r) * (p.K is Pt.Smoke or Pt.Dust ? 0.35f : 1);
                p.Vx += dx / d * f;
                p.Vy += dy / d * f - f * 0.3f;
            }
        }

        public void Update(float dt)
        {
            T += dt;
            float t = T;
            var L = List;
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var p = L[i];
                if (p.Delay > 0) { p.Delay -= dt; continue; }
                if ((p.Life -= dt) <= 0)
                {
                    if (p.K == Pt.Fire && p.Flag.HasFlag(Flags.Smokes))
                    {
                        bool big = p.Flag.HasFlag(Flags.Big);
                        Spawn(Pt.Smoke, p.X, p.Y, p.Vx * 0.6f, p.Vy * 0.6f - 12, big ? R.Range(1.3f, 2.3f) : R.Range(0.8f, 1.4f), 0, p.Size * (big ? 0.85f : 0.7f), 0, big ? Flags.Big : Flags.None);
                    }
                    L.RemoveAt(i);
                    continue;
                }
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
                    case Pt.Ember:
                        p.Vy += (-60 + MathF.Sin(t * 3 + p.Seed) * 40) * dt;
                        p.Vx += MathF.Cos(t * 2.3f + p.Seed) * 90 * dt;
                        p.Vx -= p.Vx * 1.6f * dt;
                        p.Vy -= p.Vy * 1.6f * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                    case Pt.Smoke:
                    {
                        bool big = p.Flag.HasFlag(Flags.Big);
                        p.Vy -= (big ? 46 : 32) * dt;
                        p.Vx += MathF.Sin(t * 1.3f + p.Seed) * 16 * dt;
                        p.Vx -= p.Vx * 1.6f * dt;
                        p.Vy -= p.Vy * 1.2f * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                    }
                    case Pt.Fire:
                        p.Vy -= 190 * dt;
                        p.Vx += MathF.Sin(t * 17 + p.Seed) * 60 * dt;
                        p.Vx -= p.Vx * 5 * dt;
                        p.Vy -= p.Vy * 3.2f * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                    case Pt.Dust:
                        p.Vx -= p.Vx * 4 * dt;
                        p.Vy -= p.Vy * 4 * dt + 10 * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                    case Pt.Grit:
                    {
                        p.Vy += Gravity * dt;
                        p.Vx -= p.Vx * 0.8f * dt;
                        float ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g)
                        {
                            p.Y = g - 0.5f;
                            if (p.Vy > 260 && R.Chance(0.4f)) { p.Vy = -p.Vy * 0.25f; p.Vx = p.Vx * 0.5f + R.Range(-20, 20); }
                            else { p.Vx = p.Vy = 0; p.Life = MathF.Min(p.Life, 0.5f); }
                        }
                        else p.Y = ny;
                        break;
                    }
                    case Pt.Brand:
                    {
                        p.Vy += Gravity * 0.8f * dt;
                        p.Vx -= p.Vx * 0.9f * dt;
                        if (R.Chance(0.9f))
                            Spawn(Pt.Fire, p.X, p.Y, p.Vx * 0.1f + R.Range(-15, 15), p.Vy * 0.1f - 20, R.Range(0.16f, 0.32f), 0,
                                R.Range(0.9f, 1.7f) * (0.5f + p.Life / p.Max * 0.6f), 0, R.Chance(0.4f) ? Flags.Smokes : Flags.None);
                        float ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g) { OnLand?.Invoke(p.X, g); L.RemoveAt(i); }
                        else p.Y = ny;
                        break;
                    }
                }
            }
        }

        public void Draw(PixelCanvas buf, PixelCanvas light, bool back)
        {
            if (back)
                foreach (var p in List)
                    if (p.Delay <= 0 && p.K is Pt.Smoke or Pt.Dust) DrawOne(p, buf, light);
            foreach (var p in List)
                if (p.Delay <= 0 && p.K is not (Pt.Smoke or Pt.Dust) && p.Flag.HasFlag(Flags.Back) == back) DrawOne(p, buf, light);
        }

        private void DrawOne(Part p, PixelCanvas buf, PixelCanvas light)
        {
            float k = p.Life / p.Max, ax = p.X / PX, ay = p.Y / PX;
            int px = (int)MathF.Floor(ax), py = (int)MathF.Floor(ay);
            switch (p.K)
            {
                case Pt.Smoke:
                {
                    float age = 1 - k;
                    bool big = p.Flag.HasFlag(Flags.Big);
                    float r = p.Size * (0.65f + MathF.Min(1, age * 2.5f) * 0.45f + age * (big ? 0.5f : 0.4f));
                    float a = MathF.Min(1, age * 12) * MathF.Min(1, k * 2.4f) * (big ? 0.7f : 0.58f);
                    uint c = p.Col != 0 ? Mix(p.Col, Ramp(SmokeRamp, age), age) : Ramp(SmokeRamp, age * 1.05f);
                    buf.AlphaDisc(ax, ay, r, c, a * 0.45f);
                    buf.AlphaDisc(ax + r * 0.05f, ay + r * 0.06f, r * 0.74f, c, a * 0.62f);
                    if (age < 0.5f) buf.AlphaDisc(ax - r * 0.22f, ay - r * 0.3f, r * 0.5f, Mix(c, White, 0.12f), a * 0.4f * (1 - age * 2));
                    break;
                }
                case Pt.Dust:
                    buf.AlphaDisc(ax, ay, p.Size * (0.6f + (1 - k) * 0.9f), p.Col, MathF.Pow(k, 1.3f) * 0.7f);
                    break;
                case Pt.Grit:
                    if (k > 0.25f || Bayer(px, py) < k * 4)
                    {
                        buf.Set(px, py, p.Col);
                        if (p.Size >= 2)
                        {
                            buf.Set(px + 1, py, p.Col);
                            buf.Set(px, py + 1, Scale(p.Col, 0.8f));
                            buf.Set(px + 1, py + 1, Scale(p.Col, 0.7f));
                        }
                    }
                    break;
                case Pt.Fire:
                {
                    float age = 1 - k, heat = MathF.Min(0.999f, MathF.Pow(age, 0.62f) + (p.Seed % 1) * 0.1f);
                    int idx = Math.Min(FireRamp.Length - 2, (int)MathF.Floor(heat * (FireRamp.Length - 1)));
                    float r = p.Size * (0.55f + 0.45f * MathF.Min(1, age * 7)) * (1 - age * 0.55f);
                    uint c = FireRamp[idx];
                    if (idx >= FireRamp.Length - 2) buf.AlphaDisc(ax, ay, r, c, MathF.Min(1, k * 3) * 0.85f);
                    else
                    {
                        buf.Disc(ax, ay, r, FireRamp[Math.Min(FireRamp.Length - 1, idx + 2)]);
                        buf.Disc(ax - r * 0.16f, ay - r * 0.22f, r * 0.78f, c);
                        if (r > 1.6f) buf.Disc(ax, ay - r * 0.9f, r * 0.42f, c);
                        if (idx <= 3 && r > 1.8f) buf.Disc(ax - r * 0.28f, ay - r * 0.34f, r * 0.36f, FireRamp[Math.Max(0, idx - 1)]);
                        if (idx < 6) light.Glow(ax, ay, r * 2.4f + 2, c, 0.18f * k);
                    }
                    break;
                }
                case Pt.Brand:
                    buf.Disc(ax, ay, 1.6f, Pal.DeepOrange);
                    buf.Set(px, py, MathF.Sin(T * 40 + p.Seed) > 0 ? Pal.Hot : Pal.Yellow);
                    buf.Set(px + 1, py, Pal.Yellow);
                    light.Glow(ax, ay, 7, Pal.Orange, 0.45f);
                    break;
                case Pt.Spark:
                {
                    uint hot = k > 0.6f ? Pal.Hot : k > 0.3f ? Pal.Yellow : Pal.Orange;
                    float sp = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                    if (sp == 0) sp = 1;
                    float ux = p.Vx / sp, uy = p.Vy / sp, len = MathF.Min(9, 1 + sp * 0.011f);
                    buf.Line(ax - ux * len, ay - uy * len, ax - ux * len * 0.4f, ay - uy * len * 0.4f, k > 0.5f ? hot : Pal.DeepOrange);
                    buf.Line(ax - ux * len * 0.45f, ay - uy * len * 0.45f, ax, ay, hot);
                    light.Glow(ax, ay, 3, hot, 0.45f * k);
                    break;
                }
                case Pt.Ember:
                {
                    uint c = k > 0.5f ? (MathF.Sin(T * 22 + p.Seed * 3) > -0.2f ? Pal.Yellow : Pal.Orange) : k > 0.2f ? Pal.Orange : Pal.Red;
                    buf.Set(px, py, c);
                    light.Glow(ax, ay, 3, c, 0.3f * k);
                    break;
                }
            }
        }
    }

    // ---- flashes, rings, lights, ground blazes ---------------------------------------------
    private sealed class Timed { public float X, Y, Life, Max, R, R0, R1, Thick, K, Acc, Power; public uint Color; }

    private sealed class Fx
    {
        private readonly List<Timed> _flashes = [], _rings = [], _lights = [], _blazes = [];
        private readonly Particles _ps;

        public Fx(Particles ps)
        {
            _ps = ps;
            ps.OnLand = (x, y) => Blaze(x, y - 0.5f, ps.R.Range(2.2f, 4.5f), ps.R.Range(0.55f, 0.85f));
        }

        public bool Busy => _flashes.Count + _rings.Count + _lights.Count + _blazes.Count > 0;

        private void Light(float x, float y, float r, uint color, float k, float life) => _lights.Add(new Timed { X = x, Y = y, R = r, Color = color, K = k, Life = life, Max = life });
        private void Ring(float x, float y, float r0, float r1, float life, float thick, uint color) => _rings.Add(new Timed { X = x, Y = y, R0 = r0, R1 = r1, Life = life, Max = life, Thick = thick, Color = color });
        private void Blaze(float x, float y, float life, float power) => _blazes.Add(new Timed { X = x, Y = y, Life = life, Max = life, Acc = _ps.R.Next(), Power = power });

        public void Update(float dt)
        {
            foreach (var a in (List<Timed>[])[_flashes, _rings, _lights])
                for (int i = a.Count - 1; i >= 0; i--)
                    if ((a[i].Life -= dt) <= 0) a.RemoveAt(i);
            var R = _ps.R;
            for (int i = _blazes.Count - 1; i >= 0; i--)
            {
                var b = _blazes[i];
                if ((b.Life -= dt) <= 0) { _blazes.RemoveAt(i); continue; }
                float k = b.Life / b.Max, intensity = b.Power * (k > 0.7f ? 1 : 0.35f + k / 0.7f * 0.65f);
                for (b.Acc += dt * (6 + 16 * intensity); b.Acc > 1; b.Acc--)
                {
                    float s = R.Range(1.2f, 2.8f) * intensity;
                    _ps.Spawn(Pt.Fire, b.X + R.Range(-3, 3), b.Y - s * 0.6f, R.Range(-12, 12), R.Range(-60, -25), R.Range(0.28f, 0.6f), 0, s, 0,
                        Flags.Back | (R.Chance(0.3f) ? Flags.Smokes : Flags.None));
                    if (R.Chance(0.06f)) _ps.Spawn(Pt.Ember, b.X, b.Y - 3, R.Range(-30, 30), R.Range(-90, -30), R.Range(0.6f, 1.4f), Pal.Orange, 1, 0, Flags.Back);
                }
            }
        }

        public void DrawBack(PixelCanvas buf, PixelCanvas light)
        {
            foreach (var b in _blazes)
            {
                float k = b.Life / b.Max, ax = b.X / PX, ay = b.Y / PX;
                buf.Set(MathF.Floor(ax), MathF.Floor(ay), k > 0.4f ? (MathF.Sin(b.Life * 31 + b.X) > 0 ? Pal.Yellow : Pal.Orange) : Pal.Red);
                light.Glow(ax, ay, 6 + 5 * k * b.Power, Pal.Orange, 0.3f * k * b.Power);
            }
        }

        public void DrawFront(PixelCanvas buf, PixelCanvas light)
        {
            foreach (var f in _flashes)
            {
                float k = f.Life / f.Max, ax = f.X / PX, ay = f.Y / PX, r = f.R * (0.8f + 0.2f * k);
                buf.Disc(ax, ay, r + 1.2f, Pal.DeepOrange);
                buf.Disc(ax, ay, r, k > 0.45f ? Pal.Hot : Pal.Yellow);
                buf.Disc(ax, ay, r * 0.6f, k > 0.45f ? White : Pal.Hot);
            }
            foreach (var r in _rings)
            {
                float k = r.Life / r.Max, e = 1 - MathF.Pow(k, 3);
                buf.Ring(r.X / PX, r.Y / PX, (r.R0 + (r.R1 - r.R0) * e) / PX, MathF.Max(1, r.Thick * k + 0.4f), r.Color, MathF.Min(1, k * 1.6f));
            }
            foreach (var l in _lights)
            {
                float k = l.Life / l.Max;
                light.Glow(l.X / PX, l.Y / PX, l.R / PX * (0.55f + 0.45f * k), l.Color, l.K * k * k);
            }
        }

        public void Explosion(float x, float y, float rad, float power, float fire)
        {
            var R = _ps.R;
            float ar = rad / PX;
            _flashes.Add(new Timed { X = x, Y = y, R = ar * 1.05f, Life = 0.06f, Max = 0.06f });
            Ring(x, y, rad * 0.45f, rad * 2.7f, 0.34f, 3, Pal.Yellow);
            Ring(x, y, rad * 0.3f, rad * 1.6f, 0.22f, 1.8f, Pal.Orange);
            Light(x, y, rad * 3.4f, Warm, 0.6f * power, 0.7f);
            Light(x, y, rad * 1.6f, White, 0.6f, 0.08f);
            for (int i = 0; i < 5; i++)
                _ps.Spawn(Pt.Fire, x + R.Range(-5, 5), y + R.Range(-5, 5), R.Range(-40, 40), R.Range(-80, -20), R.Range(0.18f, 0.3f), 0, R.Range(ar * 0.4f, ar * 0.55f));
            for (int i = 0; i < (int)MathF.Round(28 * power * fire); i++)
            {
                float a = R.Range(0, Tau), d = R.Range(0, rad * 0.55f), sp = R.Range(80, 420) * power;
                _ps.Spawn(Pt.Fire, x + MathF.Cos(a) * d, y + MathF.Sin(a) * d, MathF.Cos(a) * sp, MathF.Sin(a) * sp * 0.8f - 80, R.Range(0.55f, 1.15f), 0,
                    R.Range(ar * 0.22f, ar * 0.46f), 0, R.Chance(0.45f) ? Flags.Smokes | Flags.Big : Flags.None);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = R.Range(0, Tau), d = R.Range(rad * 0.1f, rad * 0.5f);
                _ps.Spawn(Pt.Smoke, x + MathF.Cos(a) * d, y + MathF.Sin(a) * d, MathF.Cos(a) * 60, MathF.Sin(a) * 40 - 60, R.Range(0.9f, 1.5f), Rgb(34, 29, 38),
                    R.Range(ar * 0.15f, ar * 0.26f), R.Range(0.06f, 0.2f), Flags.Big);
            }
            for (int i = 0; i < (int)MathF.Round(5 * fire); i++)
                _ps.Spawn(Pt.Fire, x + R.Range(-rad * 0.25f, rad * 0.25f), y, R.Range(-25, 25), R.Range(-360, -200), R.Range(0.8f, 1.3f), 0,
                    R.Range(ar * 0.24f, ar * 0.36f), R.Range(0, 0.1f), R.Chance(0.6f) ? Flags.Smokes | Flags.Big : Flags.None);
            for (int i = 0; i < (int)MathF.Round(10 * power); i++)
            {
                float a = R.Range(0, Tau), d = R.Range(0, rad * 0.7f), sp = R.Range(30, 200);
                _ps.Spawn(Pt.Smoke, x + MathF.Cos(a) * d, y + MathF.Sin(a) * d, MathF.Cos(a) * sp, MathF.Sin(a) * sp * 0.6f - 70, R.Range(1.8f, 3), 0,
                    R.Range(ar * 0.26f, ar * 0.44f), R.Range(0.12f, 0.45f), Flags.Big);
            }
            for (int i = 0; i < (int)MathF.Round(80 * power); i++)
            {
                float a = R.Range(0, Tau), sp = R.Range(320, 1200) * power;
                _ps.Spawn(Pt.Spark, x, y, MathF.Cos(a) * sp, MathF.Sin(a) * sp - 140, R.Range(0.18f, 0.7f));
            }
            for (int i = 0; i < (int)MathF.Round(40 * power * fire); i++)
            {
                float a = R.Range(0, Tau), sp = R.Range(60, 360);
                _ps.Spawn(Pt.Ember, x + MathF.Cos(a) * rad * 0.4f, y + MathF.Sin(a) * rad * 0.4f, MathF.Cos(a) * sp, MathF.Sin(a) * sp - 150, R.Range(1.2f, 3.2f), Pal.Orange);
            }
            for (int i = 0; i < (int)MathF.Round(R.Range(9, 14) * MathF.Max(0, power - 0.55f) * fire); i++)
            {
                float a = -MathF.PI / 2 + R.Range(-1.35f, 1.35f), sp = R.Range(380, 820);
                _ps.Spawn(Pt.Brand, x, y, MathF.Cos(a) * sp, MathF.Sin(a) * sp, R.Range(0.7f, 1.5f));
            }
            _ps.Impulse(x, y, rad * 2.6f, 720 * power);
        }

        public void Dust(float x, float y, int n, float spread, uint color)
        {
            var R = _ps.R;
            uint dc = Mix(color, Rgb(150, 140, 150), 0.35f);
            for (int i = 0; i < n; i++)
                _ps.Spawn(Pt.Dust, x + R.Range(-3, 3), y - 1, R.Sign() * R.Range(20, 110) * spread, -R.Range(5, 45), R.Range(0.3f, 0.65f), dc, R.Range(1.3f, 2.8f));
        }
    }

    // ---- debris ------------------------------------------------------------------------
    private sealed class Bit { public float X, Y, Vx, Vy, Life, A, Va, S; public bool Rest; }

    private sealed class Bits(Func<float, float> ground)
    {
        public readonly List<Bit> List = [];

        public void Add(float x, float y, float vx, float vy, float life, float a, float va, float s) =>
            List.Add(new Bit { X = x, Y = y, Vx = vx, Vy = vy, Life = life, A = a, Va = va, S = s });

        public void Step(float dt, Action<Bit, float> onLand)
        {
            for (int i = List.Count - 1; i >= 0; i--)
            {
                var b = List[i];
                if ((b.Life -= dt) <= 0) { List.RemoveAt(i); continue; }
                if (b.Rest) continue;
                b.Vy += Gravity * dt;
                b.Vx -= b.Vx * 0.25f * dt;
                b.Vy -= b.Vy * 0.25f * dt;
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;
                b.A += b.Va * dt;
                float floor = ground(b.X);
                if (b.Y >= floor && b.Vy > 0)
                {
                    float sp = b.Vy;
                    b.Y = floor - 0.01f;
                    onLand(b, sp);
                    if (sp > 140) { b.Vy = -sp * 0.32f; b.Vx *= 0.7f; b.Va *= 0.6f; }
                    else { b.Rest = true; b.Vx = b.Vy = 0; }
                }
            }
        }
    }
}
