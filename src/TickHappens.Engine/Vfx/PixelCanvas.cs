// Port of the Sprite Fusion pixel VFX rasterizer ("Px"). MIT License, Copyright (c) 2026 Hugo Duprez.
// See LICENSE-SpriteFusion-VFX.txt in this directory.
namespace TickHappens.Engine.Vfx;

/// <summary>Rasterizer over one buffer of a <see cref="PixelFrame"/>. Coordinates are art pixels.</summary>
public readonly struct PixelCanvas
{
    private readonly uint[] _data;
    private readonly int _w, _h, _ox, _oy;

    public PixelCanvas(PixelFrame f, uint[] data)
    {
        _data = data;
        _w = f.W;
        _h = f.H;
        _ox = f.Ox;
        _oy = f.Oy;
    }

    public static PixelCanvas ColorOf(PixelFrame f) => new(f, f.Color);
    public static PixelCanvas LightOf(PixelFrame f) => new(f, f.Light);

    private int At(float x, float y)
    {
        int ix = (int)(x - _ox), iy = (int)(y - _oy);
        return ix < 0 || iy < 0 || ix >= _w || iy >= _h ? -1 : iy * _w + ix;
    }

    public void Set(float x, float y, uint c)
    {
        int i = At(x, y);
        if (i >= 0) _data[i] = c;
    }

    public void Blend(float x, float y, uint c, float a)
    {
        int i = At(x, y);
        if (i < 0) return;
        uint d = _data[i];
        float oa = a + ((d >> 24) / 255f) * (1 - a);
        if (oa <= 0) return;
        float k = a / oa, j = 1 - k;
        uint r = (uint)((c & 255) * k + (d & 255) * j);
        uint g = (uint)(((c >> 8) & 255) * k + ((d >> 8) & 255) * j);
        uint b = (uint)(((c >> 16) & 255) * k + ((d >> 16) & 255) * j);
        _data[i] = ((uint)(oa * 255) << 24) | ((b & 255) << 16) | ((g & 255) << 8) | (r & 255);
    }

    public void Add(float x, float y, uint c, float k)
    {
        int i = At(x, y);
        if (i < 0) return;
        uint d = _data[i];
        uint r = (uint)MathF.Min(255, (d & 255) + (c & 255) * k);
        uint g = (uint)MathF.Min(255, ((d >> 8) & 255) + ((c >> 8) & 255) * k);
        uint b = (uint)MathF.Min(255, ((d >> 16) & 255) + ((c >> 16) & 255) * k);
        _data[i] = (255u << 24) | (b << 16) | (g << 8) | r;
    }

    public void Disc(float cx, float cy, float r, uint c)
    {
        if (r <= 0.5f)
        {
            Set(MathF.Round(cx), MathF.Round(cy), c);
            return;
        }
        float r2 = r * r;
        for (int y = (int)MathF.Floor(cy - r); y <= (int)MathF.Ceiling(cy + r); y++)
        {
            float dy = y + 0.5f - cy;
            for (int x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
            {
                float dx = x + 0.5f - cx;
                if (dx * dx + dy * dy <= r2) Set(x, y, c);
            }
        }
    }

    public void AlphaDisc(float cx, float cy, float r, uint c, float a)
    {
        if (a <= 0.01f) return;
        float r2 = r * r;
        for (int y = (int)MathF.Floor(cy - r); y <= (int)MathF.Ceiling(cy + r); y++)
        {
            float dy = y + 0.5f - cy;
            for (int x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
            {
                float dx = x + 0.5f - cx;
                if (dx * dx + dy * dy <= r2) Blend(x, y, c, a);
            }
        }
    }

    public void Ring(float cx, float cy, float r, float thick, uint c, float alpha = 1)
    {
        float ro = r + thick * 0.5f, ri = MathF.Max(0, r - thick * 0.5f), ro2 = ro * ro, ri2 = ri * ri;
        for (int y = (int)MathF.Floor(cy - ro); y <= (int)MathF.Ceiling(cy + ro); y++)
        {
            float dy = y + 0.5f - cy;
            for (int x = (int)MathF.Floor(cx - ro); x <= (int)MathF.Ceiling(cx + ro); x++)
            {
                float dx = x + 0.5f - cx, d2 = dx * dx + dy * dy;
                if (d2 <= ro2 && d2 >= ri2 && (alpha >= 1 || alpha > VfxMath.Bayer(x, y))) Set(x, y, c);
            }
        }
    }

    public void Line(float fx0, float fy0, float fx1, float fy1, uint c)
    {
        int x0 = (int)MathF.Round(fx0), y0 = (int)MathF.Round(fy0), x1 = (int)MathF.Round(fx1), y1 = (int)MathF.Round(fy1);
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        for (int n = 0; n < 4096; n++)
        {
            Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Additive radial glow with a quadratic falloff.</summary>
    public void Glow(float cx, float cy, float r, uint c, float k)
    {
        if (r < 1 || k <= 0.01f) return;
        int x0 = Math.Max(_ox, (int)MathF.Floor(cx - r)), x1 = Math.Min(_ox + _w - 1, (int)MathF.Ceiling(cx + r));
        int y0 = Math.Max(_oy, (int)MathF.Floor(cy - r)), y1 = Math.Min(_oy + _h - 1, (int)MathF.Ceiling(cy + r));
        for (int y = y0; y <= y1; y++)
        {
            float dy = (y + 0.5f - cy) / r;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / r, d = dx * dx + dy * dy;
                if (d >= 1) continue;
                float f = 1 - MathF.Sqrt(d);
                Add(x, y, c, f * f * k);
            }
        }
    }

    /// <summary>Ordered-dither "alpha": draws the pixel if a beats the 4x4 Bayer threshold.</summary>
    public void Dot(float x, float y, uint c, float a = 1)
    {
        int px = (int)MathF.Floor(x), py = (int)MathF.Floor(y);
        if (a >= 1 || a > VfxMath.Bayer(px, py)) Set(px, py, c);
    }
}
