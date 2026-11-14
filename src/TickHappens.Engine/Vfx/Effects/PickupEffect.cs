// C# port of Sprite Fusion "pickup" pixel VFX. MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt. Simulation units are the original's "screen" units (2 per art pixel).
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public enum PickupItem
{
    /// <summary>Faceted blue gem.</summary>
    Gem,
    /// <summary>Green orb.</summary>
    Orb,
    /// <summary>Gold coin that spins on its vertical axis.</summary>
    Coin,
}

/// <summary>Raw item art: <see cref="W"/> x <see cref="H"/> pixels packed as 0xAABBGGRR, row-major.</summary>
public sealed class PickupSprite
{
    public int W, H;
    public uint[] Data = [];
}

public sealed class PickupOptions
{
    /// <summary>Resting position in simulation units (art pixels * 2).</summary>
    public float X = 780, Y = 556;
    /// <summary>Built-in item art. Ignored when <see cref="Sprite"/> is set.</summary>
    public PickupItem Item = PickupItem.Gem;
    /// <summary>Custom item art; replaces <see cref="Item"/> and its built-in sprite.</summary>
    public PickupSprite? Sprite;
    /// <summary>Six-stop palette (white, highlight, light, mid, dark, outline). Null means the built-in palette.</summary>
    public uint[]? Palette;
    /// <summary>Magnet destination in simulation units (the original's actor feet/head midpoint). Null disables homing.</summary>
    public Func<(float X, float Y)>? Target;
    /// <summary>Fly to <see cref="Target"/> instead of popping in place. Needs <see cref="Target"/>.</summary>
    public bool Magnet = true;
    /// <summary>Seconds before the item collects itself.</summary>
    public float Wait = 0.9f;
    /// <summary>Ground line for the drop shadow. Top-down: defaults to Y (the item's own row).</summary>
    public float? Ground;
    /// <summary>Per-x ground height; overrides <see cref="Ground"/> when set.</summary>
    public Func<float, float>? GroundAt;
    public uint Seed = 1;
    public Action<float>? Shake;
}

/// <summary>Item pickup: a bobbing sprite with a glint, a magnet flight to the player and a spark burst on absorb.</summary>
public sealed class PickupEffect : IPixelEffect
{
    internal const float PX = 2, Lift = 0.1f, Fly = 0.34f;

    private static readonly uint White = Hex("#ffffff"), Shadow = Hex("#0b0814");

    private static readonly uint[] GemPalette =
        [Hex("#ffffff"), Hex("#e0faff"), Hex("#7fe0ff"), Hex("#2f9ee8"), Hex("#1d4aa8"), Hex("#101a3e")];

    private static readonly uint[] OrbPalette =
        [Hex("#ffffff"), Hex("#e6ffe0"), Hex("#8cff7a"), Hex("#2fc95a"), Hex("#157a46"), Hex("#0b2e1f")];

    private static readonly uint[] CoinPalette =
        [Hex("#fffbe8"), Hex("#ffec92"), Hex("#ffd243"), Hex("#f4a923"), Hex("#a8601a"), Hex("#3a1d0c")];

    private static readonly string[] GemRows =
    [
        "...ooooo...", "..ohwhlmo..", ".ohwhllmmo.", "olllllmmmdo", "odmmmmmmddo",
        ".odlmmmmdo.", "..odlmmdo..", "...odmdo...", "....odo....", ".....o.....",
    ];

    private static readonly string[] OrbRows =
    [
        "..ooooo..", ".ollllmo.", "olwhllmmo", "olhlllmmo", "ollllmmmo",
        "ommmmmmdo", "ommmmmddo", ".odmlddo.", "..ooooo..",
    ];

    private static readonly string[] CoinRows =
    [
        "..ooooo..", ".ohllllo.", "ohlmmmmdo", "olmhlmmdo", "olmlmmmdo",
        "olmmmmmdo", "olddmmddo", ".odddddo.", "..ooooo..",
    ];

    private readonly VfxRng _r;
    private readonly PickupSprite _spr;
    private readonly PickupItem? _kind;
    private readonly Func<(float X, float Y)>? _target;
    private readonly Func<float, float> _groundAt;
    private readonly Action<float>? _shake;
    private readonly bool _magnet;
    private readonly float _x, _y, _wait;
    private readonly uint _wh, _hi, _li, _mi, _dk;
    private readonly uint[] _burstCols, _moteCols;
    private readonly List<Spark> _sparks = [], _motes = [];
    private readonly List<Ring> _rings = [];
    private readonly List<(float X, float Y)> _trail = [];
    private float _t, _tc = float.PositiveInfinity, _popT = float.PositiveInfinity, _px, _py;
    private (float X, float Y) _from;
    private int _absorbed;

    public PickupEffect(PickupOptions o)
    {
        _x = o.X;
        _y = o.Y;
        _px = o.X;
        _py = o.Y;
        _from = (o.X, o.Y);
        _wait = o.Wait;
        _r = new VfxRng(o.Seed);
        _kind = o.Sprite is null ? o.Item : null;
        var pal = o.Palette is { Length: > 0 } custom ? custom : PaletteFor(_kind ?? PickupItem.Gem);
        _wh = At(pal, 0);
        _hi = At(pal, 1);
        _li = At(pal, 2);
        _mi = At(pal, 3);
        _dk = At(pal, 4);
        _burstCols = [_wh, _hi, _li];
        _moteCols = [_hi, _li];
        _spr = o.Sprite ?? BuildSprite(RowsFor(_kind ?? PickupItem.Gem), _wh, _hi, _li, _mi, _dk, At(pal, 5));
        if (_spr.W <= 0 || _spr.H <= 0 || _spr.Data.Length < _spr.W * _spr.H)
            throw new ArgumentException("PickupOptions.Sprite needs W > 0, H > 0 and W * H pixels.", nameof(o));
        _target = o.Target;
        _magnet = o.Magnet && o.Target is not null;
        float gy = o.Ground ?? o.Y;
        _groundAt = o.GroundAt ?? (_ => gy);
        _shake = o.Shake;
    }

    public bool Done => _popT != float.PositiveInfinity && _t - _popT > 0.5f && _sparks.Count == 0 && _motes.Count == 0;

    /// <summary>Starts the collect sequence now instead of waiting for <see cref="PickupOptions.Wait"/>.</summary>
    public void Collect()
    {
        if (_tc != float.PositiveInfinity) return;
        _tc = _t;
        _from = (_px, _py);
        if (!_magnet) Pop();
    }

    public void Update(float dt)
    {
        _t += dt;
        if (_tc == float.PositiveInfinity && _t >= _wait) Collect();
        var tg = _target?.Invoke();
        if (_tc == float.PositiveInfinity || (_magnet && _popT == float.PositiveInfinity))
        {
            float bob = MathF.Sin(_t * 3.4f) * 3;
            if (_tc == float.PositiveInfinity)
            {
                _px = _x;
                _py = _y + bob;
            }
            else if (tg is { } g)
            {
                float k = _t - _tc;
                if (k < Lift) _py = _from.Y - 8 * OutCubic(k / Lift);
                else
                {
                    float u = MathF.Pow(Clamp((k - Lift) / Fly), 2.2f), sx = _from.X, sy = _from.Y - 8;
                    float dx = g.X - sx, dy = g.Y - sy;
                    float mx = (sx + g.X) / 2, my = MathF.Min(sy, g.Y) - MathF.Max(40, MathF.Sqrt(dx * dx + dy * dy) * 0.35f);
                    _px = (1 - u) * (1 - u) * sx + 2 * (1 - u) * u * mx + u * u * g.X;
                    _py = (1 - u) * (1 - u) * sy + 2 * (1 - u) * u * my + u * u * g.Y;
                    _trail.Insert(0, (_px, _py));
                    if (_trail.Count > 7) _trail.RemoveAt(_trail.Count - 1);
                    if (u >= 1) Pop();
                }
            }
        }
        else if (_trail.Count > 0) _trail.RemoveAt(_trail.Count - 1);

        var R = _r;
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            float age = s.Max - s.Life;
            s.Tr.Insert(0, (s.X, s.Y));
            if (s.Tr.Count > 5) s.Tr.RemoveAt(s.Tr.Count - 1);
            if ((s.Life -= dt) <= 0) { _sparks.RemoveAt(i); continue; }
            if (tg is { } tgt && age > 0.12f)
            {
                float dx = tgt.X - s.X, dy = tgt.Y - s.Y, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d == 0) d = 1;
                float want = 380 + 900 * age, k = MathF.Min(1, dt * (3 + 22 * age));
                s.Vx += (dx / d * want - s.Vx) * k;
                s.Vy += (dy / d * want - s.Vy) * k;
                if (d < 9)
                {
                    _sparks.RemoveAt(i);
                    if (_absorbed++ % 3 == 0) _rings.Add(new Ring { X = tgt.X, Y = tgt.Y, T = _t, R = 12 });
                    for (int j = 0; j < 2; j++)
                    {
                        float mx = tgt.X + R.Range(-10, 10), my = tgt.Y + R.Range(-14, 10);
                        float vx = R.Range(-12, 12), vy = -R.Range(30, 80), life = R.Range(0.4f, 0.8f);
                        int sz = R.Chance(0.3f) ? 1 : 0;
                        uint c = R.Pick(_moteCols);
                        _motes.Add(new Spark { X = mx, Y = my, Vx = vx, Vy = vy, Life = life, Max = 0.8f, S = sz, C = c });
                    }
                    continue;
                }
            }
            else
            {
                s.Vx -= s.Vx * 3 * dt;
                s.Vy -= s.Vy * 3 * dt;
                s.Vy += 60 * dt;
            }
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
        }

        for (int i = _motes.Count - 1; i >= 0; i--)
        {
            var m = _motes[i];
            if ((m.Life -= dt) <= 0) { _motes.RemoveAt(i); continue; }
            m.Vx -= m.Vx * 1.5f * dt;
            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;
        }

        for (int i = _rings.Count - 1; i >= 0; i--)
            if (_t - _rings[i].T > 0.4f) _rings.RemoveAt(i);
    }

    public void Draw(PixelFrame f, VfxLayer layer)
    {
        var buf = PixelCanvas.ColorOf(f);
        var light = PixelCanvas.LightOf(f);
        float ax = _px / PX, ay = _py / PX, g = _groundAt(_x);
        bool alive = _popT == float.PositiveInfinity;
        if (layer != VfxLayer.Front)
        {
            if (alive && _tc == float.PositiveInfinity && float.IsFinite(g))
            {
                float h = Clamp((g - _py) / 80), rx = (_spr.W / 2f + 1) * (1 - 0.35f * h);
                for (int x = (int)MathF.Floor(-rx); x <= rx; x++)
                {
                    float u = MathF.Abs(x / rx);
                    float a = (0.8f - 0.4f * h) * (1 - u * u);
                    buf.Dot(_x / PX + x, g / PX, Shadow, a);
                    if (MathF.Abs(x) < rx * 0.6f) buf.Dot(_x / PX + x, g / PX + 1, Shadow, a * 0.6f);
                }
            }
            Orbit(buf, light, ax, ay, alive, false);
        }
        if (layer == VfxLayer.Back) return;
        for (int i = 1; i < _trail.Count; i++)
            buf.Line(_trail[i - 1].X / PX, _trail[i - 1].Y / PX, _trail[i].X / PX, _trail[i].Y / PX, i < 3 ? _li : _mi);
        if (alive)
        {
            float flying = _tc != float.PositiveInfinity ? Clamp((_t - _tc - Lift) / Fly) : 0;
            float spin = _kind == PickupItem.Coin ? MathF.Cos(_t * 5.2f) : 1;
            float sx = MathF.Max(0.15f, MathF.Abs(spin)) * (1 - 0.25f * flying), sy = 1 - 0.25f * flying;
            float lift = _tc != float.PositiveInfinity && _t - _tc < Lift ? OutCubic((_t - _tc) / Lift) : 0;
            BlitScaled(buf, _spr, ax, ay, sx * (1 - 0.15f * lift), sy * (1 + 0.2f * lift),
                MathF.Abs(spin) < 0.3f && _kind == PickupItem.Coin ? _dk : 0u);
            float gl = (_t + 0.4f) % 1.3f / 1.3f;
            if (gl < 0.22f) Star(buf, light, ax - _spr.W * 0.22f, ay - _spr.H * 0.2f, Round(2.5f * MathF.Sin(gl / 0.22f * MathF.PI)), _wh);
            Orbit(buf, light, ax, ay, alive, true);
            light.Glow(ax, ay, 14 + 2 * MathF.Sin(_t * 4), _mi, 0.45f + 0.4f * flying);
            light.Glow(ax, ay, 5, _wh, 0.25f);
        }
        else
        {
            float k = _t - _popT;
            if (k < 0.1f) BlitScaled(buf, _spr, ax, ay, k < 0.05f ? 1.5f : 0.7f, k < 0.05f ? 0.6f : 1.45f, k < 0.05f ? 0u : _wh);
            if (k < 0.08f) buf.Disc(ax, ay, 7 * (1 - k / 0.08f) + 1, _wh);
            if (k < 0.3f) light.Glow(ax, ay, 30, _li, 0.9f * (1 - k / 0.3f));
        }
        foreach (var r in _rings)
        {
            float k = (_t - r.T) / 0.4f, e = OutCubic(k);
            buf.Ring(r.X / PX, r.Y / PX, 2 + r.R / PX * e * 2, MathF.Max(1, 2 * (1 - k)), k < 0.3f ? _wh : _li, 1.2f - k);
            light.Glow(r.X / PX, r.Y / PX, 16, _li, 0.5f * (1 - k));
        }
        foreach (var s in _sparks)
        {
            float k = s.Life / s.Max;
            for (int i = 1; i < s.Tr.Count; i++)
                buf.Dot(s.Tr[i].X / PX, s.Tr[i].Y / PX, i < 2 ? _li : _mi, 1 - i / 5f);
            Star(buf, light, s.X / PX, s.Y / PX, Round(s.S * MathF.Min(1, k * 3)), s.C);
        }
        foreach (var m in _motes)
        {
            float k = m.Life / m.Max;
            if (k * (0.6f + 0.4f * MathF.Sin(m.Life * 30)) > 0.25f) Star(buf, light, m.X / PX, m.Y / PX, m.S, m.C);
        }
    }

    // ---- item art --------------------------------------------------------------------------
    private static uint[] PaletteFor(PickupItem k) => k switch
    {
        PickupItem.Orb => OrbPalette,
        PickupItem.Coin => CoinPalette,
        _ => GemPalette,
    };

    private static string[] RowsFor(PickupItem k) => k switch
    {
        PickupItem.Orb => OrbRows,
        PickupItem.Coin => CoinRows,
        _ => GemRows,
    };

    private static PickupSprite BuildSprite(string[] rows, uint wh, uint hi, uint li, uint mi, uint dk, uint ol)
    {
        int h = rows.Length, w = 0;
        foreach (var r in rows) w = Math.Max(w, r.Length);
        var data = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            string row = rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                uint c = row[x] switch { 'w' => wh, 'h' => hi, 'l' => li, 'm' => mi, 'd' => dk, 'o' => ol, _ => 0u };
                if (c != 0) data[y * w + x] = c;
            }
        }
        return new PickupSprite { W = w, H = h, Data = data };
    }

    private static void BlitScaled(PixelCanvas buf, PickupSprite s, float cx, float cy, float sx, float sy, uint tint = 0)
    {
        float hw = s.W * sx / 2, hh = s.H * sy / 2;
        for (int y = (int)MathF.Floor(cy - hh); y <= (int)MathF.Ceiling(cy + hh); y++)
            for (int x = (int)MathF.Floor(cx - hw); x <= (int)MathF.Ceiling(cx + hw); x++)
            {
                int u = (int)MathF.Floor((x + 0.5f - cx) / sx + s.W / 2f), v = (int)MathF.Floor((y + 0.5f - cy) / sy + s.H / 2f);
                if (u < 0 || v < 0 || u >= s.W || v >= s.H) continue;
                uint c = s.Data[v * s.W + u];
                if (c >> 24 != 0) buf.Set(x, y, tint != 0 ? tint : c);
            }
    }

    private static void Star(PixelCanvas buf, PixelCanvas light, float x, float y, int arm, uint c)
    {
        int px = (int)MathF.Floor(x), py = (int)MathF.Floor(y);
        for (int d = 1; d <= arm; d++)
        {
            uint cc = d == arm && arm > 1 ? Scale(c, 0.8f) : c;
            buf.Set(px + d, py, cc);
            buf.Set(px - d, py, cc);
            buf.Set(px, py + d, cc);
            buf.Set(px, py - d, cc);
        }
        buf.Set(px, py, arm > 0 ? White : c);
        light.Glow(x, y, 3 + arm * 2.5f, c, 0.4f);
    }

    // ---- helpers ---------------------------------------------------------------------------
    private void Orbit(PixelCanvas buf, PixelCanvas light, float ax, float ay, bool alive, bool front)
    {
        if (!alive) return;
        for (int i = 0; i < 2; i++)
        {
            float a = _t * 2.6f + i * MathF.PI, ox = MathF.Cos(a) * 9, oy = MathF.Sin(a) * 2.6f - 1;
            if ((MathF.Sin(a) > 0) != front) continue;
            buf.Dot(ax + ox, ay + oy, front ? _hi : _mi);
            light.Glow(ax + ox, ay + oy, 4, _li, 0.35f);
        }
    }

    private void Pop()
    {
        _popT = _t;
        _shake?.Invoke(0.05f);
        _rings.Add(new Ring { X = _px, Y = _py, T = _t, R = 20 });
        var R = _r;
        for (int i = 0; i < 14; i++)
        {
            float a = (float)i / 14 * Tau + R.Range(-0.2f, 0.2f), sp = R.Range(150, 300);
            float life = R.Range(0.75f, 1.05f);
            int sz = R.Chance(0.4f) ? 2 : 1;
            uint c = R.Pick(_burstCols);
            _sparks.Add(new Spark
            {
                X = _px, Y = _py, Vx = MathF.Cos(a) * sp, Vy = MathF.Sin(a) * sp - 40,
                Life = life, Max = 1.05f, S = sz, C = c,
            });
        }
    }

    /// <summary>JS Math.round: halves round up, unlike MathF.Round's banker's rounding.</summary>
    private static int Round(float v) => (int)MathF.Floor(v + 0.5f);

    private static uint At(uint[] p, int i) => p[Math.Clamp(i, 0, p.Length - 1)];

    // ---- particles -------------------------------------------------------------------------
    private sealed class Spark
    {
        public float X, Y, Vx, Vy, Life, Max;
        public int S;
        public uint C;
        public readonly List<(float X, float Y)> Tr = [];
    }

    private sealed class Ring
    {
        public float X, Y, T, R;
    }
}
