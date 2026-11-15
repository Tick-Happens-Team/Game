using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public sealed class DashSplashOptions
{
    /// <summary>Dash start in art pixels (world space).</summary>
    public float X, Y;
    /// <summary>Unit dash direction.</summary>
    public float Dx = 1, Dy;
    /// <summary>Dash length in art pixels and duration in seconds.</summary>
    public float Distance = 46, Seconds = 0.16f;
    public uint Seed = 1;
    /// <summary>Four-stop ramp from the hottest to the deepest color.</summary>
    public uint[]? Ramp;
}

/// <summary>
/// Dash burst: a squashed shockwave ring where the dash starts, a crown of droplets thrown back and out that arc
/// under gravity and ripple where they land, speed streaks along the dash path, and a smaller splash at the end.
/// Original TickHappens effect on the shared pixel VFX core; art-pixel units, seeded, cosmetic only.
/// </summary>
public sealed class DashSplashEffect : IPixelEffect
{
    private const float Gravity = 340, Life = 0.85f;
    private static readonly uint[] DefaultRamp = [Hex("#ffffff"), Hex("#a6f3ff"), Hex("#3dc2ff"), Hex("#1d56f2")];

    private struct Drop
    {
        public float X, Y, Z, Vx, Vy, Vz, Delay, Landed;
        public bool Big;
    }

    private readonly DashSplashOptions _o;
    private readonly uint[] _ramp;
    private readonly Drop[] _drops;
    private readonly float[] _streaks;
    private readonly float _ex, _ey, _px, _py;
    private float _t;

    public bool Done => _t >= Life;

    public DashSplashEffect(DashSplashOptions o)
    {
        _o = o;
        _ramp = o.Ramp is { Length: >= 4 } r ? r : DefaultRamp;
        var rng = new VfxRng(o.Seed);
        _ex = o.X + o.Dx * o.Distance;
        _ey = o.Y + o.Dy * o.Distance;
        _px = -o.Dy;
        _py = o.Dx;
        float back = MathF.Atan2(-o.Dy, -o.Dx), fwd = MathF.Atan2(o.Dy, o.Dx);
        _drops = new Drop[40];
        for (int i = 0; i < _drops.Length; i++)
        {
            bool end = i >= 28;  // the last ten splash forward at the end of the dash
            float a = (end ? fwd : back) + (rng.Next() * 2 - 1) * (end ? 1.1f : 1.75f);
            float sp = end ? 30 + rng.Next() * 55 : 45 + rng.Next() * 85;
            _drops[i] = new Drop
            {
                X = end ? _ex : o.X, Y = end ? _ey : o.Y, Z = 2,
                Vx = MathF.Cos(a) * sp, Vy = MathF.Sin(a) * sp * 0.6f, Vz = 55 + rng.Next() * 75,
                Delay = end ? o.Seconds : rng.Next() * 0.04f, Landed = -1, Big = rng.Next() < 0.45f,
            };
        }
        _streaks = new float[4];
        for (int i = 0; i < _streaks.Length; i++) _streaks[i] = (rng.Next() * 2 - 1) * 6;
    }

    public void Update(float dt)
    {
        _t += dt;
        for (int i = 0; i < _drops.Length; i++)
        {
            ref var d = ref _drops[i];
            if (_t < d.Delay || d.Landed >= 0) continue;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vz -= Gravity * dt;
            d.Z += d.Vz * dt;
            d.Vx *= 1 - 1.2f * dt;
            d.Vy *= 1 - 1.2f * dt;
            if (d.Z <= 0) { d.Z = 0; d.Landed = _t; }
        }
    }

    private void Ellipse(PixelCanvas c, float cx, float cy, float r, float squash, uint col, float alpha)
    {
        int n = Math.Max(12, (int)(r * 5));
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * MathF.Tau;
            c.Dot(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r * squash, col, alpha);
        }
    }

    public void Draw(PixelFrame frame, VfxLayer layer)
    {
        var c = PixelCanvas.ColorOf(frame);
        var l = PixelCanvas.LightOf(frame);
        float t = _t;

        // Shockwave at the start: fast, squashed to the ground plane, a white core for the first frames.
        float k = Math.Clamp(t / 0.38f, 0, 1);
        if (k < 1)
        {
            float ease = 1 - (1 - k) * (1 - k) * (1 - k), r = 3 + 30 * ease, fade = 1 - k;
            Ellipse(c, _o.X, _o.Y, r, 0.45f, k < 0.15f ? _ramp[0] : _ramp[1], fade * 1.4f);
            Ellipse(c, _o.X, _o.Y, r * 0.8f, 0.45f, _ramp[2], fade);
            if (k < 0.5f) Ellipse(c, _o.X, _o.Y, r + 1.5f, 0.45f, _ramp[3], fade * 0.8f);
            l.Glow(_o.X, _o.Y - 2, 18 + 20 * ease, _ramp[2], 0.45f * fade);
        }

        // Speed streaks drawn in along the path while dashing, then fading from the tail.
        float grow = Math.Clamp(t / _o.Seconds, 0, 1), fadeS = 1 - Math.Clamp((t - _o.Seconds) / 0.3f, 0, 1);
        if (fadeS > 0)
            for (int i = 0; i < _streaks.Length; i++)
            {
                float off = _streaks[i], tail = Math.Clamp((t - _o.Seconds) / 0.3f, 0, 1) * 0.9f;
                float h = -6 - MathF.Abs(off) * 0.6f;  // streaks hover at body height
                float x0 = _o.X + _px * off, y0 = _o.Y + _py * off * 0.5f + h;
                int steps = (int)(_o.Distance * grow);
                for (int s = (int)(steps * tail); s <= steps; s++)
                {
                    float u = s / MathF.Max(1, _o.Distance);
                    float a = fadeS * (0.15f + 0.6f * u);
                    uint col = u > 0.8f ? _ramp[0] : u > 0.4f ? _ramp[1] : _ramp[2];
                    c.Dot(x0 + _o.Dx * s, y0 + _o.Dy * s, col, a);
                }
                
            }

        // End splash ring.
        float ke = Math.Clamp((t - _o.Seconds) / 0.3f, 0, 1);
        if (t > _o.Seconds && ke < 1)
        {
            float r = 2 + 16 * (1 - (1 - ke) * (1 - ke));
            Ellipse(c, _ex, _ey, r, 0.45f, _ramp[1], (1 - ke) * 1.2f);
            l.Glow(_ex, _ey - 2, 22, _ramp[2], 0.35f * (1 - ke));
        }

        // Droplets: a bright head over a short trail and a ground shadow; a ripple where they land.
        foreach (var d in _drops)
        {
            if (t < d.Delay) continue;
            if (d.Landed < 0)
            {
                float sy = d.Y - d.Z;
                c.Dot(d.X, d.Y, _ramp[3], 0.5f);
                c.Set(d.X - d.Vx * 0.012f, sy + d.Vz * 0.012f, _ramp[2]);
                c.Set(d.X, sy, d.Big ? _ramp[0] : _ramp[1]);
                if (d.Big)
                {
                    c.Set(d.X + 1, sy, _ramp[1]);
                    c.Set(d.X, sy - 1, _ramp[1]);
                    c.Set(d.X + 1, sy - 1, _ramp[2]);
                }
                if (d.Big && t - d.Delay > 0.05f) l.Add(d.X, sy, _ramp[2], 0.2f);
            }
            else
            {
                float a = (t - d.Landed) / 0.3f;
                if (a < 1) Ellipse(c, d.X, d.Y, 1 + (d.Big ? 5 : 3) * a, 0.5f, _ramp[2], 1 - a);
            }
        }
    }
}
