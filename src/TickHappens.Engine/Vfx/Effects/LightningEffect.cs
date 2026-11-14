// C# port of Sprite Fusion "lightning" pixel VFX. MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt. Simulation units are the original's "screen" units (2 per art pixel).
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

/// <summary>Which of the original's four lightning variants to run.</summary>
public enum LightningKind
{
    /// <summary>Cloud-to-ground strike: storm bank, three return strokes, scorch and debris.</summary>
    Strike,
    /// <summary>Radial star burst at a point.</summary>
    Impact,
    /// <summary>Chain of arcs hopping between points.</summary>
    Arc,
    /// <summary>Sustained crackle around a radius.</summary>
    Crackle,
}

/// <summary>Options for <see cref="LightningEffect"/>. Unset positions fall back to the original's per-kind defaults.</summary>
public sealed class LightningOptions
{
    public LightningKind Kind = LightningKind.Strike;

    /// <summary>Origin x in simulation units. Defaults: strike 790, impact 780, arc 520, crackle 640.</summary>
    public float? X;

    /// <summary>Impact y (default 524), arc source y (default 520), crackle centre y (default 540).
    /// For a strike it is the fallback ground line (default 600).</summary>
    public float? Y;

    /// <summary>Ground line in simulation units. Unset means <see cref="Y"/> for a strike and no ground for the other kinds.</summary>
    public float? Ground;

    /// <summary>Per-x ground height; overrides <see cref="Ground"/> when set.</summary>
    public Func<float, float>? GroundAt;

    /// <summary>Seven-stop voltage ramp (core, hot, pale, mid, deep, dark, shadow). Unset means the original palette.</summary>
    public uint[]? Colors;

    public uint Seed = 1;

    /// <summary>Cloud base y for a strike. Unset means ground - 408.</summary>
    public float? Top;

    /// <summary>Return strokes for a strike, clamped to 1..3.</summary>
    public float Strokes = 3;

    /// <summary>Draw the storm cloud bank for a strike.</summary>
    public bool Clouds = true;

    /// <summary>Impact scale.</summary>
    public float Size = 1;

    /// <summary>Impact plasma particle count.</summary>
    public float Sparks = 64;

    /// <summary>Arc source point. Unset means (<see cref="X"/>, <see cref="Y"/>) with the arc defaults.</summary>
    public (float X, float Y)? From;

    /// <summary>Arc hop points. Unset means (640,600), (760,600), (880,600).</summary>
    public (float X, float Y)[]? To;

    /// <summary>Arc lifetime (default 0.9) or crackle lifetime (default 1.8).</summary>
    public float? Duration;

    /// <summary>Crackle radius in simulation units.</summary>
    public float Radius = 36;

    /// <summary>Optional arc source that follows a moving hand.</summary>
    public Func<(float X, float Y)>? Hand;

    public Action<float>? Shake;

    /// <summary>Screen flash: strength and an "r,g,b" string.</summary>
    public Action<float, string>? Flash;

    /// <summary>Ground ripple: x, y, inner radius, outer radius, duration, amplitude, width.</summary>
    public Action<float, float, float, float, float, float, float>? Ripple;

    /// <summary>Ambient darkening, 0..1.</summary>
    public Action<float>? Darken;

    /// <summary>Gameplay cue, e.g. "hurt" with { t = 0.04 }.</summary>
    public Action<string, IReadOnlyDictionary<string, float>?>? Cue;
}

public sealed class LightningEffect : IPixelEffect
{
    internal const float PX = 2, Gravity = 1900;

    private static readonly uint[] Volt =
        [Hex("#ffffff"), Hex("#e9f8ff"), Hex("#a3dcff"), Hex("#5eb0ff"), Hex("#5b6cff"), Hex("#4733c4"), Hex("#2a1b78")];

    private static readonly uint[] Plasma = [Hex("#ffffff"), Hex("#7ff3ff"), Hex("#1ab8e6")];
    private static readonly uint[] SparkRamp = [Hex("#fff3a6"), Hex("#ffe26b"), Hex("#ff8a2a"), Hex("#f2561d")];

    private static readonly uint[] HeatRamp =
    [
        Hex("#ffffff"), Hex("#fff3a6"), Hex("#ffe26b"), Hex("#ffc23d"), Hex("#ff8a2a"),
        Hex("#f2561d"), Hex("#c9281f"), Hex("#8a1f17"), Hex("#4f1a13"), Hex("#2a120e"),
    ];

    private static readonly uint[] SmokeRamp = [Hex("#7b6f72"), Hex("#5d5660"), Hex("#433d48"), Hex("#2b2630"), Hex("#1a171e")];

    private static readonly uint SmokeTint = Hex("#5b5566"), DustColor = Hex("#7a6e8e"), Soot = Hex("#0d0a12"), Soot2 = Hex("#17121d");
    private static readonly uint CloudBody = Hex("#0f0c1d"), CloudUnder = Hex("#1b1631"), CloudRim = Hex("#29224a");

    private static readonly Dictionary<string, float> HurtData = new() { ["t"] = 0.04f };

    private readonly Ctx _c;
    private readonly Run _run;
    private readonly Particles _ps;
    private readonly List<Light> _lights;

    public LightningEffect(LightningOptions o)
    {
        var R = new VfxRng(o.Seed);
        uint[] v = o.Colors is { Length: > 0 } custom ? custom : Volt;
        float fallback = o.Kind == LightningKind.Strike ? o.Y ?? 600 : float.PositiveInfinity;
        Func<float, float> groundAt;
        if (o.GroundAt is { } ga) groundAt = ga;
        else if (o.Ground is { } g) groundAt = _ => g;
        else groundAt = _ => fallback;
        uint[] plasma = o.Colors is not null ? [At(v, 0), At(v, 2), At(v, 4)] : Plasma;
        _ps = new Particles(R, groundAt, plasma);
        _lights = [];
        _c = new Ctx(o, R, v, _ps, _lights, groundAt);
        _run = o.Kind switch
        {
            LightningKind.Impact => new ImpactRun(_c),
            LightningKind.Arc => new ArcRun(_c),
            LightningKind.Crackle => new CrackleRun(_c),
            _ => new StrikeRun(_c),
        };
    }

    public bool Done => _run.Over && _ps.List.Count == 0 && _lights.Count == 0;

    public void Update(float dt)
    {
        _run.Update(dt);
        _ps.Update(dt);
        StepLights(_lights, dt);
    }

    public void Draw(PixelFrame f, VfxLayer layer)
    {
        var buf = PixelCanvas.ColorOf(f);
        var light = PixelCanvas.LightOf(f);
        if (layer != VfxLayer.Front)
        {
            _run.Back(buf, light, f);
            _ps.Draw(buf, light, back: true);
        }
        if (layer != VfxLayer.Back)
        {
            _run.Front(buf, light, f);
            _ps.Draw(buf, light, back: false);
            DrawLights(light, _lights);
        }
    }

    /// <summary>Ends the arc/crackle early; the strike and impact variants ignore it, as in the original.</summary>
    public void Stop() => _run.Stop();

    // ---- shared context ------------------------------------------------------------------
    private sealed class Ctx
    {
        public readonly LightningOptions O;
        public readonly VfxRng R;
        public readonly uint[] V;
        public readonly Particles Ps;
        public readonly List<Light> Lights;
        public readonly Func<float, float> GroundAt;

        public Ctx(LightningOptions o, VfxRng r, uint[] v, Particles ps, List<Light> lights, Func<float, float> groundAt)
        {
            O = o;
            R = r;
            V = v;
            Ps = ps;
            Lights = lights;
            GroundAt = groundAt;
        }

        public uint Col(int i) => At(V, i);
    }

    private abstract class Run
    {
        public abstract bool Over { get; }
        public abstract void Update(float dt);
        public virtual void Back(PixelCanvas buf, PixelCanvas light, PixelFrame f) { }
        public abstract void Front(PixelCanvas buf, PixelCanvas light, PixelFrame f);
        public virtual void Stop() { }
    }

    // ---- strike --------------------------------------------------------------------------
    private sealed class BoltStroke
    {
        public float T, K;
        public Bolt B = null!;
    }

    private readonly struct Hit(BoltStroke s, float a, float k)
    {
        public readonly BoltStroke S = s;
        public readonly float A = a, K = k;
    }

    private sealed class Cloud
    {
        public float X, Y, R;
    }

    private sealed class Crawl
    {
        public Bolt B = null!;
        public float T;
    }

    private sealed class StrikeRun : Run
    {
        private const float Lead = 0.42f;

        private readonly Ctx _c;
        private readonly float _X, _G, _cx, _cy, _top;
        private readonly List<BoltStroke> _strokes = [];
        private readonly List<Cloud> _clouds = [];
        private readonly List<Crawl> _crawls = [];
        private float _t, _flash;
        private int _struck = -1;

        public StrikeRun(Ctx c)
        {
            _c = c;
            var o = c.O;
            var R = c.R;
            _X = o.X ?? 790;
            _G = c.GroundAt(_X);
            _cx = _X / PX;
            _cy = _G / PX;
            _top = (o.Top ?? _G - 408) / PX;

            // All three bolts are generated up front, exactly like the original's array literal.
            BoltStroke[] all =
            [
                new() { T = 0.62f, K = 1, B = MakeBolt(R, _cx + 18, _top, _cx, _cy, 0.2f, 0.75f) },
                new() { T = 1.02f, K = 0.85f, B = MakeBolt(R, _cx - 14, _top, _cx + 1, _cy, 0.2f, 0.6f) },
                new() { T = 1.36f, K = 0.55f, B = MakeBolt(R, _cx + 6, _top, _cx, _cy, 0.24f, 0.4f) },
            ];
            int n = Math.Max(1, Math.Min(3, (int)o.Strokes));
            for (int i = 0; i < n; i++) _strokes.Add(all[i]);

            for (int i = 0; i < 24; i++)
                _clouds.Add(new Cloud
                {
                    X = (_X - 490) / PX + i * 15 + Hash(i, 1) * 8,
                    Y = _top + 22 + Hash(i, 2) * 10,
                    R = 12 + Hash(i, 3) * 10,
                });
        }

        public override bool Over => _t > 3;

        private Hit? Now()
        {
            for (int i = _strokes.Count - 1; i >= 0; i--)
            {
                var s = _strokes[i];
                float a = _t - s.T;
                if (a < 0) continue;
                float f = a < 0.03f ? 1 : a < 0.06f ? 0.55f : a < 0.1f ? 0.95f : a < 0.14f ? 0.4f : a < 0.17f ? 0.7f : 0;
                return new Hit(s, a, f * s.K);
            }
            return null;
        }

        public override void Update(float dt)
        {
            var o = _c.O;
            var R = _c.R;
            var ps = _c.Ps;
            _t += dt;
            o.Darken?.Invoke(0.55f * Smooth(-0.3f, 0.35f, _t) * (1 - Smooth(2.1f, 2.9f, _t)) * (1 - 0.7f * _flash));
            _flash = MathF.Max(0, _flash - dt * 6);

            for (int i = 0; i < _strokes.Count; i++)
            {
                var s = _strokes[i];
                if (_struck >= i || _t < s.T) continue;
                _struck = i;
                _flash = s.K;
                o.Flash?.Invoke(s.K, "225,236,255");
                o.Shake?.Invoke(0.5f * s.K);
                o.Ripple?.Invoke(_X, _G - 4, 20, 260 * s.K, 0.5f, 0.18f * s.K, 22);

                int n = Round(60 * s.K);
                for (int j = 0; j < n; j++)
                {
                    float a = -MathF.PI / 2 + R.Range(-1.4f, 1.4f), sp = R.Range(240, 980) * s.K;
                    ps.Spawn(j % 3 != 0 ? Pt.Plasma : Pt.Spark, _X, _G - 3, MathF.Cos(a) * sp, MathF.Sin(a) * sp, R.Range(0.2f, 0.7f));
                }
                for (int j = 0; j < 5; j++)
                    ps.Spawn(Pt.Smoke, _X + R.Range(-12, 12), _G - 6, R.Range(-20, 20), R.Range(-50, -20), R.Range(1.4f, 2.4f),
                        Mix(SmokeTint, _c.Col(5), 0.3f), R.Range(3, 6), R.Range(0.1f, 0.5f));
                Dust(ps, R, _X, _G, 8, 1.6f);
                _c.Lights.Add(new Light { X = _X, Y = _G - 60, R = 320, Color = _c.Col(4), K = 0.7f * s.K, Life = 0.3f, Max = 0.3f });
            }

            if (_struck >= 0 && R.Chance(0.6f * Clamp(1 - (_t - _strokes[_struck].T) / 0.4f)))
            {
                float side = R.Sign(), l = R.Range(10, 28);
                _crawls.Add(new Crawl
                {
                    B = MakeBolt(R, _cx + side * R.Range(1, 4), _cy - 0.5f, _cx + side * l, _cy - R.Range(0, 2), 0.35f, 0.2f, 0.6f),
                    T = _t,
                });
            }
            for (int i = _crawls.Count - 1; i >= 0; i--) if (_t - _crawls[i].T > 0.08f) _crawls.RemoveAt(i);

            if (_t > _strokes[0].T && _t < 2.6f && R.Chance(0.12f))
                ps.Spawn(Pt.Smoke, _X + R.Range(-10, 10), _G - 2, R.Range(-8, 8), R.Range(-34, -18), R.Range(1.4f, 2.2f), 0, R.Range(2, 3.5f));
        }

        public override void Back(PixelCanvas buf, PixelCanvas light, PixelFrame f)
        {
            var first = _strokes[0];
            var s = Now();
            if (_struck >= 0) Scorch(buf, light, _cx, _cy, 20, MathF.Pow(Clamp(1 - (_t - first.T) / 2.2f), 1.3f));
            if (!_c.O.Clouds) return;

            float lead = _t > first.T - Lead && _t < first.T ? 0.35f + 0.35f * MathF.Sin(_t * 50) : 0;
            float lit = MathF.Max(s?.K ?? 0,
                MathF.Max(lead * 0.5f, MathF.Max(0, MathF.Sin(_t * 9 + 1)) * 0.2f * Smooth(0, 0.3f, _t) * (_t < 0.5f ? 1 : 0)));
            Storm(buf, light, f, _clouds, _cx, lit, _t, _c);
        }

        public override void Front(PixelCanvas buf, PixelCanvas light, PixelFrame f)
        {
            var first = _strokes[0];
            var s = Now();

            if (_t > first.T - Lead && _t < first.T)
            {
                float reach = _top + (_cy - _top) * MathF.Pow(MathF.Floor(((_t - first.T + Lead) / Lead) * 9) / 9, 1.3f);
                foreach (var p in first.B.Paths)
                    for (int i = 0; i < p.Pts.Count - 1; i++)
                    {
                        var (ax, ay) = p.Pts[i];
                        var (bx, by) = p.Pts[i + 1];
                        if (by > reach) break;
                        buf.Line(ax, ay, bx, by, p.W >= 1 ? _c.Col(3) : _c.Col(5));
                        if (i % 4 == 0) light.Glow(ax, ay, 6, _c.Col(4), 0.25f);
                    }
            }

            if (s is { K: > 0 } hit)
            {
                Stroke(buf, light, hit.S.B, hit.K, _c, 1);
                buf.Disc(_cx, _cy - 1, 4 * hit.K + 1.5f, _c.Col(5));
                buf.Disc(_cx, _cy - 1, 4 * hit.K + 0.5f, _c.Col(2));
                buf.Disc(_cx, _cy - 1, 2.6f * hit.K, _c.Col(0));
                light.Glow(_cx, _cy - 10, 110, _c.Col(4), 0.55f * hit.K);
                light.Glow(_cx, _cy - 4, 26, _c.Col(2), 0.45f * hit.K);
            }
            else if (s is { } after && after.A < 0.45f)
            {
                float k = 1 - (after.A - 0.17f) / 0.28f;
                foreach (var p in after.S.B.Paths)
                    if (p.W >= 1)
                        foreach (var (x, y) in p.Pts)
                            buf.Dot(x, y, _c.Col(5), k * 0.8f);
            }

            foreach (var c in _crawls) DrawBolt(buf, light, c.B, _c, 1 - (_t - c.T) / 0.1f, 0.4f);
            if (_struck >= 0) light.Glow(_cx, _cy - 2, 20, _c.Col(3), 0.35f * Clamp(1 - (_t - first.T) / 1.5f));
        }
    }

    // ---- impact --------------------------------------------------------------------------
    private sealed class ImpactRun : Run
    {
        private readonly Ctx _c;
        private readonly float _S, _ax, _ay;
        private readonly List<Bolt> _star = [];
        private readonly List<Crawl> _arcs = [];
        private float _t;
        private int _starAt = -1;

        public ImpactRun(Ctx c)
        {
            _c = c;
            var o = c.O;
            var R = c.R;
            float X = o.X ?? 780, Y = o.Y ?? 524;
            _S = o.Size;
            _ax = X / PX;
            _ay = Y / PX;

            o.Flash?.Invoke(0.7f, "200,226,255");
            o.Shake?.Invoke(0.4f * MathF.Min(1.5f, _S));
            for (int i = 0; i < o.Sparks; i++)
            {
                float a = R.Range(0, Tau), sp = R.Range(240, 900) * MathF.Sqrt(_S);
                c.Ps.Spawn(Pt.Plasma, X, Y, MathF.Cos(a) * sp, MathF.Sin(a) * sp - 100, R.Range(0.2f, 0.75f));
            }
            for (int i = 0; i < 5; i++)
                c.Ps.Spawn(Pt.Smoke, X + R.Range(-14, 14) * _S, Y + R.Range(-10, 10) * _S, R.Range(-10, 10), R.Range(-30, -12),
                    R.Range(1.2f, 2), Mix(SmokeTint, c.Col(4), 0.35f), R.Range(3, 5) * _S, R.Range(0.08f, 0.25f));
        }

        public override bool Over => _t > 1.6f;

        public override void Update(float dt)
        {
            var R = _c.R;
            var ps = _c.Ps;
            _t += dt;
            int f = (int)MathF.Floor(_t * 30);

            if (_t < 0.3f && f != _starAt)
            {
                _starAt = f;
                float len = _t < 0.07f ? OutBack(_t / 0.07f, 2.2f) : 1 - (_t - 0.07f) / 0.3f;
                _star.Clear();
                for (int i = 0; i < 7; i++)
                {
                    float a = (float)i / 7 * Tau + Hash(i, 3) * 0.6f + R.Range(-0.12f, 0.12f);
                    float L = (18 + 18 * Hash(i, 5)) * MathF.Max(0.15f, len) * _S;
                    _star.Add(MakeBolt(R, _ax, _ay, _ax + MathF.Cos(a) * L, _ay + MathF.Sin(a) * L, 0.28f, 0.5f));
                }
            }

            if (_t > 0.04f && _t < 0.9f && R.Chance(0.5f * (1 - _t / 0.9f)))
            {
                float a = R.Range(0, Tau), d = R.Range(6, 26) * _S, a2 = a + R.Range(-1.4f, 1.4f), l = R.Range(6, 14) * _S;
                float x0 = _ax + MathF.Cos(a) * d, y0 = _ay + MathF.Sin(a) * d;
                _arcs.Add(new Crawl
                {
                    B = MakeBolt(R, x0, y0, x0 + MathF.Cos(a2) * l, y0 + MathF.Sin(a2) * l, 0.4f, 0.2f, 0.6f),
                    T = _t,
                });
                if (R.Chance(0.4f)) ps.Spawn(Pt.Plasma, x0 * PX, y0 * PX, R.Range(-200, 200), R.Range(-260, 40), R.Range(0.12f, 0.3f));
            }
            for (int i = _arcs.Count - 1; i >= 0; i--) if (_t - _arcs[i].T > 0.07f) _arcs.RemoveAt(i);
        }

        public override void Front(PixelCanvas buf, PixelCanvas light, PixelFrame f)
        {
            if (_t < 0.05f)
            {
                buf.Disc(_ax, _ay, (10 - _t * 60) * _S, _c.Col(3));
                buf.Disc(_ax, _ay, (8 - _t * 60) * _S, _c.Col(1));
                buf.Disc(_ax, _ay, (6 - _t * 50) * _S, _c.Col(0));
            }
            if (_t < 0.32f)
            {
                float k = _t / 0.32f, r = (4 + 34 * (1 - MathF.Pow(1 - k, 3))) * _S;
                buf.Ring(_ax, _ay, r, MathF.Max(1, 3 * (1 - k)), k < 0.4f ? _c.Col(1) : _c.Col(3), 1.2f - k);
            }
            if (_t < 0.3f) foreach (var b in _star) DrawBolt(buf, light, b, _c, 1 - _t / 0.36f, 0.6f);
            foreach (var a in _arcs) DrawBolt(buf, light, a.B, _c, 1 - (_t - a.T) / 0.1f, 0.35f);

            if (_t > 0.15f && _t < 1.5f)
                for (int i = 0; i < 10 * (1 - _t / 1.5f); i++)
                {
                    int s = (int)MathF.Floor(_t * 40) * 13 + i;
                    float a = Hash(s, 1) * Tau, d = (4 + Hash(s, 2) * 30) * _S;
                    float x = _ax + MathF.Cos(a) * d, y = _ay + MathF.Sin(a) * d;
                    buf.Dot(x, y, Hash(s, 3) < 0.3f ? _c.Col(0) : _c.Col(2));
                    if (Hash(s, 4) < 0.4f) buf.Dot(x + (Hash(s, 5) < 0.5f ? 1 : -1), y + 1, _c.Col(3));
                }

            float strobe = _t < 0.3f ? ((int)MathF.Floor(_t * 60) % 2 != 0 ? 0.7f : 1) : 0.5f + 0.5f * MathF.Sin(_t * 70);
            light.Glow(_ax, _ay, 70 * _S * MathF.Max(0, 1 - _t / 1.7f), _c.Col(3), 0.9f * MathF.Exp(-_t * 4) * strobe);
            light.Glow(_ax, _ay, 26 * _S, _c.Col(0), 0.9f * MathF.Exp(-_t * 9));
        }
    }

    // ---- arc -----------------------------------------------------------------------------
    private sealed class Link
    {
        public float On;
        public bool Hit;
        public Bolt? B;
    }

    private sealed class Mark
    {
        public float X, Y, T;
    }

    private sealed class ArcRun : Run
    {
        private const float LinkTime = 0.05f;

        private readonly Ctx _c;
        private readonly (float X, float Y)[] _hops;
        private readonly List<Link> _links = [];
        private readonly List<Crawl> _crawls = [];
        private readonly List<Mark> _marks = [];
        private float _t, _stopAt;
        private int _at = -1;

        public ArcRun(Ctx c)
        {
            _c = c;
            var o = c.O;
            _hops = o.To ?? [(640, 600), (760, 600), (880, 600)];
            _stopAt = o.Duration ?? 0.9f;
            for (int i = 0; i < _hops.Length; i++) _links.Add(new Link { On = i * LinkTime });
        }

        public override bool Over => _t > _stopAt + 1.7f;

        public override void Stop() => _stopAt = MathF.Min(_stopAt, _t);

        private (float X, float Y) Src()
        {
            var o = _c.O;
            if (o.Hand is { } h) return h();
            return o.From ?? (o.X ?? 520, o.Y ?? 520);
        }

        private (float X, float Y)[] PointsNow()
        {
            var p = new (float X, float Y)[_hops.Length + 1];
            p[0] = Src();
            for (int i = 0; i < _hops.Length; i++) p[i + 1] = _hops[i];
            return p;
        }

        private float Level()
        {
            float flick = _t < 0.12f
                ? ((int)MathF.Floor(_t * 60) % 3 == 1 ? 0.6f : 1)
                : 0.8f + 0.2f * MathF.Sin(_t * 61) * MathF.Sin(_t * 23);
            return _t < _stopAt ? flick : flick * Clamp(1 - (_t - _stopAt) / 0.18f);
        }

        public override void Update(float dt)
        {
            var o = _c.O;
            var R = _c.R;
            var ps = _c.Ps;
            _t += dt;
            var P = PointsNow();
            float k = Level();
            int f = (int)MathF.Floor(_t * 30);

            for (int i = 0; i < _links.Count; i++)
            {
                var l = _links[i];
                if (l.Hit || _t < l.On) continue;
                l.Hit = true;
                var (x, y) = P[i + 1];
                float g = _c.GroundAt(x);
                bool onGround = MathF.Abs(g - y) < 8;
                for (int j = 0; j < 14; j++)
                {
                    float a = (onGround ? -MathF.PI / 2 : 0) + R.Range(-1.4f, 1.4f) + (onGround ? 0 : R.Range(0, Tau)), sp = R.Range(200, 620);
                    ps.Spawn(Pt.Plasma, x, y - 2, MathF.Cos(a) * sp, MathF.Sin(a) * sp, R.Range(0.15f, 0.45f));
                }
                if (onGround)
                {
                    Dust(ps, R, x, g, 4, 0.9f);
                    _marks.Add(new Mark { X = x, Y = g, T = _t });
                }
                _c.Lights.Add(new Light { X = x, Y = y, R = 120, Color = _c.Col(3), K = 0.8f, Life = 0.2f, Max = 0.2f });
                if (i == 0)
                {
                    o.Flash?.Invoke(0.35f, "215,232,255");
                    o.Shake?.Invoke(0.2f);
                }
                else o.Shake?.Invoke(0.08f);
            }

            if (f != _at && k > 0)
            {
                _at = f;
                for (int i = 0; i < _links.Count; i++)
                {
                    var l = _links[i];
                    if (!l.Hit) continue;
                    var (x0, y0) = P[i];
                    var (x1, y1) = P[i + 1];
                    bool g0 = MathF.Abs(_c.GroundAt(x0) - y0) < 8, g1 = MathF.Abs(_c.GroundAt(x1) - y1) < 8;
                    l.B = g0 && g1
                        ? Hop(R, x0 / PX, y0 / PX, x1 / PX, y1 / PX)
                        : MakeBolt(R, x0 / PX, y0 / PX, x1 / PX, y1 / PX, 0.22f, 0.45f);
                }
            }

            if (k > 0.3f)
            {
                for (int i = 0; i < _links.Count; i++)
                {
                    var l = _links[i];
                    if (!l.Hit || !R.Chance(0.3f)) continue;
                    var (x, y) = P[i + 1];
                    float a = R.Range(0, Tau), d = R.Range(4, 14);
                    _crawls.Add(new Crawl
                    {
                        B = MakeBolt(R, x / PX, y / PX, x / PX + MathF.Cos(a) * d,
                            y / PX + MathF.Sin(a) * d * (MathF.Abs(_c.GroundAt(x) - y) < 8 ? 0.3f : 1), 0.4f, 0.2f, 0.6f),
                        T = _t,
                    });
                }
                if (R.Chance(0.5f))
                {
                    int i = R.Int(0, _links.Count - 1);
                    float u = R.Next();
                    var (x0, y0) = P[i];
                    var (x1, y1) = P[i + 1];
                    if (_links[i].Hit)
                        ps.Spawn(Pt.Plasma, Lerp(x0, x1, u), Lerp(y0, y1, u), R.Range(-120, 120), R.Range(-160, 40), R.Range(0.1f, 0.25f));
                }
            }

            for (int i = _crawls.Count - 1; i >= 0; i--) if (_t - _crawls[i].T > 0.07f) _crawls.RemoveAt(i);
        }

        public override void Back(PixelCanvas buf, PixelCanvas light, PixelFrame f)
        {
            foreach (var m in _marks) Scorch(buf, light, m.X / PX, m.Y / PX, 6, MathF.Pow(Clamp(1 - (_t - m.T) / 1.6f), 1.3f));
        }

        public override void Front(PixelCanvas buf, PixelCanvas light, PixelFrame f)
        {
            var P = PointsNow();
            float k = Level();
            if (k > 0)
            {
                foreach (var l in _links) if (l.Hit && l.B is not null) Stroke(buf, light, l.B, k, _c, 0.7f);
                for (int i = 0; i < _links.Count; i++)
                {
                    if (!_links[i].Hit) continue;
                    var (x, y) = P[i + 1];
                    float ax = x / PX, ay = y / PX, r = (2.2f + MathF.Sin(_t * 47 + i) * 0.6f) * k;
                    buf.Disc(ax, ay, r + 1.4f, _c.Col(5));
                    buf.Disc(ax, ay, r + 0.6f, _c.Col(2));
                    buf.Disc(ax, ay, r * 0.6f, _c.Col(0));
                    light.Glow(ax, ay, 20, _c.Col(3), 0.5f * k);
                }
                var (hx, hy) = P[0];
                float hr = (2 + MathF.Sin(_t * 53) * 0.5f) * k;
                buf.Disc(hx / PX, hy / PX, hr + 1, _c.Col(3));
                buf.Disc(hx / PX, hy / PX, hr, _c.Col(0));
                light.Glow(hx / PX, hy / PX, 16, _c.Col(2), 0.6f * k);
            }
            else if (_t - _stopAt < 0.45f)
            {
                float g = 1 - (_t - _stopAt - 0.18f) / 0.27f;
                foreach (var l in _links)
                    if (l.B is not null)
                        foreach (var p in l.B.Paths)
                            if (p.W >= 1)
                                foreach (var (x, y) in p.Pts)
                                    buf.Dot(x, y, _c.Col(5), g * 0.7f);
            }

            foreach (var c in _crawls) DrawBolt(buf, light, c.B, _c, 1 - (_t - c.T) / 0.09f, 0.3f);
        }
    }

    // ---- crackle -------------------------------------------------------------------------
    private sealed class Arc
    {
        public Bolt B = null!;
        public float T, Life;
    }

    private sealed class CrackleRun : Run
    {
        private readonly Ctx _c;
        private readonly float _rad;
        private readonly List<Arc> _arcs = [];
        private readonly List<(float X, float Y)> _edge = [];
        private (float X, float Y) _centre;
        private float _t, _stopAt, _shock, _nextShock = 0.12f;
        private int _at = -1;

        public CrackleRun(Ctx c)
        {
            _c = c;
            _rad = c.O.Radius / PX;
            _stopAt = c.O.Duration ?? 1.8f;
        }

        public override bool Over => _t > _stopAt + 0.4f && _arcs.Count == 0;

        public override void Stop() => _stopAt = MathF.Min(_stopAt, _t);

        private float Level() => Smooth(0, 0.08f, _t) * (_t < _stopAt ? 1 : Clamp(1 - (_t - _stopAt) / 0.3f));

        private void Outline()
        {
            var R = _c.R;
            _edge.Clear();
            float cx = (_c.O.X ?? 640) / PX, cy = (_c.O.Y ?? 540) / PX;
            for (int i = 0; i < 48; i++)
            {
                float a = R.Range(0, Tau), d = _rad * MathF.Sqrt(R.Next());
                _edge.Add((cx + MathF.Cos(a) * d, cy + MathF.Sin(a) * d));
            }
            float sx = 0, sy = 0;
            foreach (var (x, y) in _edge)
            {
                sx += x;
                sy += y;
            }
            _centre = (sx / _edge.Count, sy / _edge.Count);
        }

        private (float X, float Y) PickEdge(VfxRng R)
        {
            float u = R.Next();
            return _edge.Count == 0 ? (0, 0) : _edge[(int)MathF.Floor(u * _edge.Count)];
        }

        public override void Update(float dt)
        {
            var o = _c.O;
            var R = _c.R;
            var ps = _c.Ps;
            _t += dt;
            _shock = MathF.Max(0, _shock - dt);
            int f = (int)MathF.Floor(_t * 30);
            float k = Level();

            if (f != _at && f % 2 == 0 && k > 0.05f && _t < _stopAt)
            {
                _at = f;
                Outline();
                // The original re-evaluates R.int(0, 1) on every loop check, so the RNG stream depends on it.
                for (int j = 0; j < 2 + R.Int(0, 1); j++)
                {
                    var a = PickEdge(R);
                    float ux = a.X - _centre.X, uy = a.Y - _centre.Y, l = MathF.Sqrt(ux * ux + uy * uy);
                    if (l == 0) l = 1;
                    float ang = MathF.Atan2(uy / l, ux / l) + R.Range(-1.2f, 1.2f), d = R.Range(6, 15);
                    var b = (X: a.X + MathF.Cos(ang) * d, Y: a.Y + MathF.Sin(ang) * d);
                    for (int tries = 0; tries < 10; tries++)
                    {
                        var c = PickEdge(R);
                        if (MathF.Sqrt((c.X - b.X) * (c.X - b.X) + (c.Y - b.Y) * (c.Y - b.Y)) < 4 &&
                            MathF.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y)) > 4) b = c;
                    }
                    _arcs.Add(new Arc { B = MakeBolt(R, a.X, a.Y, b.X, b.Y, 0.42f, 0.35f), T = _t, Life = R.Range(0.05f, 0.09f) });
                    if (R.Chance(0.4f)) ps.Spawn(Pt.Plasma, b.X * PX, b.Y * PX, R.Range(-140, 140), R.Range(-200, 20), R.Range(0.1f, 0.25f));
                }

                var low = _edge[0];
                foreach (var p in _edge) if (p.Y > low.Y) low = p;
                float g = _c.GroundAt(low.X * PX) / PX;
                if (R.Chance(0.25f) && g - low.Y < 14)
                    _arcs.Add(new Arc { B = MakeBolt(R, low.X, low.Y, low.X + R.Range(-9, 9), g - 0.5f, 0.45f, 0.3f), T = _t, Life = 0.07f });
            }

            if (_t >= _nextShock && _t < _stopAt)
            {
                _nextShock = _t + R.Range(0.5f, 0.8f);
                _shock = 0.05f;
                o.Cue?.Invoke("hurt", HurtData);
                o.Shake?.Invoke(0.05f);
                for (int i = 0; i < 6; i++)
                {
                    float a = R.Range(0, Tau), sp = R.Range(150, 400);
                    var (x, y) = PickEdge(R);
                    ps.Spawn(Pt.Plasma, x * PX, y * PX, MathF.Cos(a) * sp, MathF.Sin(a) * sp - 60, R.Range(0.1f, 0.3f));
                }
            }

            for (int i = _arcs.Count - 1; i >= 0; i--) if (_t - _arcs[i].T > _arcs[i].Life) _arcs.RemoveAt(i);
        }

        public override void Front(PixelCanvas buf, PixelCanvas light, PixelFrame f)
        {
            float k = Level();
            foreach (var a in _arcs)
                Stroke(buf, light, a.B, MathF.Max(0.35f, (1 - (_t - a.T) / (a.Life + 0.04f)) * k), _c, 0.45f);
            if (k > 0.02f)
                light.Glow(_centre.X, _centre.Y, 34, _c.Col(3), (0.18f + 0.12f * MathF.Sin(_t * 57) + (_shock > 0 ? 0.35f : 0)) * k);
        }
    }

    // ---- bolts ---------------------------------------------------------------------------
    private sealed class BoltPath
    {
        public List<(float X, float Y)> Pts = [];
        public float W;
    }

    private sealed class Bolt
    {
        public List<BoltPath> Paths = [];
    }

    private static Bolt MakeBolt(VfxRng R, float x0, float y0, float x1, float y1, float jag = 0.32f, float forks = 0.35f, float w = 1, int depth = 0)
    {
        var outBolt = new Bolt();
        float L = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var pts = new List<(float X, float Y)> { (x0, y0), (x1, y1) };
        float off = L * jag;
        while (pts.Count < 2 + L / 3)
        {
            var nxt = new List<(float X, float Y)> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var (ax, ay) = pts[i];
                var (bx, by) = pts[i + 1];
                float l = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (l == 0) l = 1;
                float o = R.Range(-off, off);
                nxt.Add(((ax + bx) / 2 + (-(by - ay) / l) * o, (ay + by) / 2 + ((bx - ax) / l) * o));
                nxt.Add((bx, by));
            }
            pts = nxt;
            off *= 0.52f;
        }
        outBolt.Paths.Add(new BoltPath { Pts = pts, W = w });

        if (depth < 2)
            for (int i = 2; i < pts.Count - 3; i++)
                if (R.Chance(forks / (1 + depth) / MathF.Max(4, pts.Count / 6f)))
                {
                    var (ax, ay) = pts[i];
                    var (bx, by) = pts[Math.Min(pts.Count - 1, i + 3)];
                    float a = MathF.Atan2(by - ay, bx - ax) + R.Sign() * R.Range(0.35f, 0.9f);
                    float l = (L - (float)i / pts.Count * L) * R.Range(0.25f, 0.55f);
                    outBolt.Paths.AddRange(MakeBolt(R, ax, ay, ax + MathF.Cos(a) * l, ay + MathF.Sin(a) * l, jag * 1.1f, forks, w * 0.55f, depth + 1).Paths);
                }
        return outBolt;
    }

    private static Bolt Hop(VfxRng R, float x0, float y0, float x1, float y1)
    {
        float mx = (x0 + x1) / 2 + R.Range(-4, 4);
        float my = MathF.Min(y0, y1) - MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * R.Range(0.16f, 0.28f);
        var a = MakeBolt(R, x0, y0, mx, my, 0.2f, 0.4f);
        var b = MakeBolt(R, mx, my, x1, y1, 0.2f, 0.4f);
        var pts = new List<(float X, float Y)>(a.Paths[0].Pts);
        pts.AddRange(b.Paths[0].Pts.Skip(1));
        var paths = new List<BoltPath> { new() { Pts = pts, W = 1 } };
        paths.AddRange(a.Paths.Skip(1));
        paths.AddRange(b.Paths.Skip(1));
        return new Bolt { Paths = paths };
    }

    private static void DrawBolt(PixelCanvas buf, PixelCanvas light, Bolt b, Ctx c, float k = 1, float glowK = 0.5f)
    {
        foreach (var p in b.Paths)
        {
            bool thick = p.W >= 1;
            for (int i = 0; i < p.Pts.Count - 1; i++)
            {
                var (ax, ay) = p.Pts[i];
                var (bx, by) = p.Pts[i + 1];
                float l = MathF.Max(1, MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)));
                for (float d = 0; d <= l; d += 0.5f)
                {
                    float x = ax + (bx - ax) * d / l, y = ay + (by - ay) * d / l;
                    if (k > 0.45f && thick)
                    {
                        buf.Dot(x + 1.5f, y, c.Col(4), (k - 0.45f) * 1.6f);
                        buf.Dot(x - 1.5f, y, c.Col(4), (k - 0.45f) * 1.6f);
                        buf.Dot(x, y + 1.5f, c.Col(4), (k - 0.45f) * 1.6f);
                        buf.Dot(x, y - 1.5f, c.Col(4), (k - 0.45f) * 1.6f);
                    }
                    if (k > 0.25f && (thick || p.W > 0.4f))
                    {
                        buf.Dot(x + 1, y, c.Col(2));
                        buf.Dot(x, y + 1, c.Col(2));
                    }
                    buf.Set(MathF.Floor(x), MathF.Floor(y), thick && k > 0.3f ? c.Col(0) : c.Col(2));
                }
                if (i % 3 == 0) light.Glow(ax, ay, (thick ? 9 : 5) * (0.5f + k * 0.5f), c.Col(3), glowK * k * p.W);
            }
        }
    }

    private static void Stroke(PixelCanvas buf, PixelCanvas light, Bolt b, float k, Ctx c, float s)
    {
        for (int pass = 0; pass < 3; pass++)
            foreach (var p in b.Paths)
            {
                bool trunk = p.W >= 1;
                if (!trunk && (k < 0.5f || (pass == 2 && p.W < 0.5f))) continue;
                for (int i = 0; i < p.Pts.Count - 1; i++)
                {
                    var (ax, ay) = p.Pts[i];
                    var (bx, by) = p.Pts[i + 1];
                    float l = MathF.Max(1, MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)));
                    for (float d = 0; d <= l; d += 0.5f)
                    {
                        float x = ax + (bx - ax) * d / l, y = ay + (by - ay) * d / l;
                        if (pass == 0) buf.Disc(x, y, (trunk ? (k > 0.6f ? 2.1f : 1.6f) : 1.1f) * s, c.Col(5));
                        else if (pass == 1) buf.Disc(x, y, (trunk ? (k > 0.6f ? 1.3f : 0.8f) : 0.5f) * s, trunk ? c.Col(2) : c.Col(3));
                        else if (trunk && k > 0.3f) buf.Set(MathF.Floor(x), MathF.Floor(y), c.Col(0));
                    }
                    if (pass == 0 && i % 3 == 0) light.Glow(ax, ay, (trunk ? 14 : 7) * s, c.Col(3), 0.5f * k * p.W);
                }
            }
    }

    // ---- storm bank, scorch, dust --------------------------------------------------------
    private static void Storm(PixelCanvas buf, PixelCanvas light, PixelFrame f, List<Cloud> clouds, float cx, float lit, float t, Ctx c)
    {
        for (int x = f.Ox; x < f.Ox + f.W; x++)
        {
            float yb = -1e9f, yb2 = -1e9f;
            for (int i = 0; i < clouds.Count; i++)
            {
                var cl = clouds[i];
                float dx = x + 0.5f - (cl.X + Noise(t * 0.4f + cl.X, 9) * 2);
                if (MathF.Abs(dx) >= cl.R) continue;
                float y = cl.Y + MathF.Sqrt(cl.R * cl.R - dx * dx);
                if (y > yb) yb = y;
                float y2 = cl.Y - 9 + i % 3 * 2 + MathF.Sqrt(cl.R * cl.R - dx * dx) * 0.7f;
                if (i % 2 != 0 && y2 > yb2) yb2 = y2;
            }
            if (yb < -1e8f) continue;

            float l = lit * MathF.Max(0, 1 - MathF.Abs(x - cx) / 120);
            for (int y = f.Oy; y < yb; y++) buf.Set(x, y, CloudBody);
            buf.Dot(x, yb, CloudBody, 0.5f);
            buf.Dot(x, yb + 1, CloudBody, 0.25f);
            buf.Dot(x, yb - 1, l > 0.55f ? c.Col(3) : l > 0.2f ? c.Col(5) : CloudRim);
            buf.Dot(x, yb - 2, l > 0.55f ? c.Col(5) : CloudUnder, 0.85f);
            buf.Dot(x, yb - 3, CloudUnder, 0.45f);
            if (yb2 > -1e8f && yb2 < yb - 4)
            {
                buf.Dot(x, yb2, l > 0.4f ? c.Col(5) : CloudUnder, 0.9f);
                buf.Dot(x, yb2 - 1, CloudUnder, 0.4f);
            }
            if ((x & 7) == 0 && l > 0.05f) light.Glow(x, yb - 2, 22, c.Col(4), 0.3f * l);
        }
    }

    private static void Scorch(PixelCanvas buf, PixelCanvas light, float cx, float cy, float rx, float T0)
    {
        float ry = MathF.Max(2.5f, rx * 0.09f);
        for (int y = (int)MathF.Floor(cy); y <= cy + ry; y++)
            for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 1) continue;
                float k = Hash(x * 31 + y * 7);
                float T = T0 * (1.25f - d) - k * 0.35f;
                if (T > 0.06f)
                {
                    uint col = HeatRamp[Math.Min(HeatRamp.Length - 1, (int)MathF.Floor((1 - MathF.Min(1, T)) * (HeatRamp.Length - 1)))];
                    buf.Set(x, y, col);
                    if ((x & 3) == 0) light.Glow(x + 0.5f, y, 6, col, 0.18f * T);
                }
                else if (d < 0.95f - k * 0.3f || Bayer(x, y) < 1 - d) buf.Set(x, y, d < 0.5f ? Soot : Soot2);
            }
    }

    private static void Dust(Particles ps, VfxRng R, float x, float y, int n, float spread)
    {
        for (int i = 0; i < n; i++)
            ps.Spawn(Pt.Dust, x + R.Range(-3, 3), y - 1, R.Sign() * R.Range(20, 110) * spread, -R.Range(5, 45), R.Range(0.3f, 0.65f),
                DustColor, R.Range(1.3f, 2.8f));
    }

    // ---- particles -----------------------------------------------------------------------
    private enum Pt : byte { Spark, Plasma, Smoke, Dust }

    private sealed class Part
    {
        public Pt K;
        public float X, Y, Vx, Vy, Life, Max, Size, Seed, Delay;
        public uint Col;
    }

    private sealed class Particles(VfxRng r, Func<float, float> ground, uint[] plasma)
    {
        public readonly List<Part> List = [];
        public readonly VfxRng R = r;
        public float T;

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
                switch (p.K)
                {
                    case Pt.Spark:
                    case Pt.Plasma:
                    {
                        p.Vy += Gravity * 0.45f * dt;
                        p.Vx -= p.Vx * 2.2f * dt;
                        p.Vy -= p.Vy * 2.2f * dt;
                        float g = ground(p.X), ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g) { p.Vy = -p.Vy * 0.45f; p.Vx *= 0.7f; p.Y = g - 0.5f; }
                        else p.Y = ny;
                        break;
                    }
                    case Pt.Smoke:
                        p.Vy -= 32 * dt;
                        p.Vx += MathF.Sin(t * 1.3f + p.Seed) * 16 * dt;
                        p.Vx -= p.Vx * 1.6f * dt;
                        p.Vy -= p.Vy * 1.2f * dt;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                    case Pt.Dust:
                        p.Vx -= p.Vx * 4 * dt;
                        p.Vy -= p.Vy * 4 * dt + 10 * dt;
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
                if (p.Delay > 0) continue;
                if ((p.K is Pt.Smoke or Pt.Dust) != back) continue;
                DrawOne(p, buf, light);
            }
        }

        private void DrawOne(Part p, PixelCanvas buf, PixelCanvas light)
        {
            float k = p.Life / p.Max, ax = p.X / PX, ay = p.Y / PX;
            switch (p.K)
            {
                case Pt.Smoke:
                {
                    float age = 1 - k, r = p.Size * (0.65f + MathF.Min(1, age * 2.5f) * 0.45f + age * 0.4f);
                    float a = MathF.Min(1, age * 12) * MathF.Min(1, k * 2.4f) * 0.58f;
                    uint c = p.Col != 0 ? Mix(p.Col, Ramp(SmokeRamp, age), age) : Ramp(SmokeRamp, age * 1.05f);
                    buf.AlphaDisc(ax, ay, r, c, a * 0.45f);
                    buf.AlphaDisc(ax + r * 0.05f, ay + r * 0.06f, r * 0.74f, c, a * 0.62f);
                    if (age < 0.5f) buf.AlphaDisc(ax - r * 0.22f, ay - r * 0.3f, r * 0.5f, Mix(c, Volt[0], 0.12f), a * 0.4f * (1 - age * 2));
                    break;
                }
                case Pt.Dust:
                    buf.AlphaDisc(ax, ay, p.Size * (0.6f + (1 - k) * 0.9f), p.Col, MathF.Pow(k, 1.3f) * 0.7f);
                    break;
                case Pt.Spark:
                case Pt.Plasma:
                {
                    uint[] C = p.K == Pt.Plasma ? plasma : SparkRamp;
                    uint hot = p.K == Pt.Plasma
                        ? (k > 0.5f ? C[0] : k > 0.25f ? C[1] : C[2])
                        : (k > 0.6f ? C[0] : k > 0.3f ? C[1] : C[2]);
                    float sp = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                    if (sp == 0) sp = 1;
                    float ux = p.Vx / sp, uy = p.Vy / sp, len = MathF.Min(9, 1 + sp * 0.011f);
                    buf.Line(ax - ux * len, ay - uy * len, ax - ux * len * 0.4f, ay - uy * len * 0.4f, k > 0.5f ? hot : C[C.Length - 1]);
                    buf.Line(ax - ux * len * 0.45f, ay - uy * len * 0.45f, ax, ay, hot);
                    light.Glow(ax, ay, 3, hot, 0.45f * k);
                    break;
                }
            }
        }
    }

    // ---- lights --------------------------------------------------------------------------
    private sealed class Light
    {
        public float X, Y, R, K, Life, Max;
        public uint Color;
    }

    private static void StepLights(List<Light> lights, float dt)
    {
        for (int i = lights.Count - 1; i >= 0; i--) if ((lights[i].Life -= dt) <= 0) lights.RemoveAt(i);
    }

    private static void DrawLights(PixelCanvas light, List<Light> lights)
    {
        foreach (var l in lights)
        {
            float k = l.Life / l.Max;
            light.Glow(l.X / PX, l.Y / PX, l.R / PX * (0.55f + 0.45f * k), l.Color, l.K * k * k);
        }
    }

    // ---- scalar helpers ------------------------------------------------------------------
    private static uint At(uint[] a, int i) => a[Math.Min(a.Length - 1, i)];

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

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>JS Math.round: halves round up, unlike MathF.Round's banker's rounding.</summary>
    private static int Round(float v) => (int)MathF.Floor(v + 0.5f);
}
