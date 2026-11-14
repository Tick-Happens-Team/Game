// Port of the Sprite Fusion pixel VFX helpers. MIT License, Copyright (c) 2026 Hugo Duprez.
// See LICENSE-SpriteFusion-VFX.txt in this directory.
namespace TickHappens.Engine.Vfx;

/// <summary>Deterministic mulberry32 generator, bit-identical to the TypeScript original.</summary>
public sealed class VfxRng
{
    private uint _s;

    public VfxRng(uint seed = 1) => _s = seed == 0 ? 1u : seed;

    public float Next()
    {
        unchecked
        {
            uint t = _s += 0x6d2b79f5;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (float)((t ^ (t >> 14)) / 4294967296.0);
        }
    }

    public float Range(float a, float b) => a + (b - a) * Next();
    public int Int(int a, int b) => (int)MathF.Floor(Range(a, b + 1));
    public float Sign() => Next() < 0.5f ? -1 : 1;
    public bool Chance(float p) => Next() < p;
    public T Pick<T>(IReadOnlyList<T> a) => a[(int)MathF.Floor(Next() * a.Count)];
}

public static class VfxMath
{
    public const float Tau = MathF.PI * 2;

    private static readonly int[] BayerTable = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>4x4 ordered-dither threshold in (0,1).</summary>
    public static float Bayer(int x, int y) => (BayerTable[((y & 3) << 2) | (x & 3)] + 0.5f) / 16f;

    public static uint Rgb(float r, float g, float b, float a = 255) =>
        (((uint)(int)a & 255) << 24) | (((uint)(int)b & 255) << 16) | (((uint)(int)g & 255) << 8) | ((uint)(int)r & 255);

    public static uint Hex(string h)
    {
        int v = Convert.ToInt32(h.TrimStart('#'), 16);
        return Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    public static uint Scale(uint c, float k) =>
        Rgb(MathF.Min(255, (c & 255) * k), MathF.Min(255, ((c >> 8) & 255) * k), MathF.Min(255, ((c >> 16) & 255) * k), c >> 24);

    public static uint Mix(uint c1, uint c2, float t)
    {
        float u = 1 - t;
        static float Ch(uint c, int s) => (c >> s) & 255;
        return Rgb(Ch(c1, 0) * u + Ch(c2, 0) * t, Ch(c1, 8) * u + Ch(c2, 8) * t, Ch(c1, 16) * u + Ch(c2, 16) * t, Ch(c1, 24) * u + Ch(c2, 24) * t);
    }

    public static uint Ramp(IReadOnlyList<uint> stops, float t)
    {
        float u = MathF.Max(0, MathF.Min(0.9999f, t)) * (stops.Count - 1);
        int i = (int)MathF.Floor(u);
        return Mix(stops[i], stops[i + 1], u - i);
    }

    public static float Clamp(float v, float a = 0, float b = 1) => v < a ? a : v > b ? b : v;
    public static float OutCubic(float t) => 1 - MathF.Pow(1 - Clamp(t), 3);

    /// <summary>Integer hash in [0,1), matching Math.imul semantics of the original.</summary>
    public static float Hash(double n, int k = 0)
    {
        unchecked
        {
            int v = (int)(long)(n + 1 + k * 7919);
            uint h = (uint)(v * (int)2654435761u);
            return (h % 100000u) / 100000f;
        }
    }

    /// <summary>Smooth 1D value noise in [-1,1].</summary>
    public static float Noise(float x, int seed = 0)
    {
        float i = MathF.Floor(x), f = x - i, u = f * f * (3 - 2 * f);
        return Hash(i, seed) * 2 - 1 + (Hash(i + 1, seed) - Hash(i, seed)) * 2 * u;
    }

    /// <summary>Dithered ellipse ring; keep() filters by angle (used to split back/front halves).</summary>
    public static void RingE(PixelCanvas buf, float cx, float cy, float rx, float ry, float thick, uint c, float a, Func<float, bool> keep)
    {
        int n = (int)MathF.Ceiling(MathF.Max(rx, ry) * Tau * 1.3f);
        for (int j = 0; j < n; j++)
        {
            float ang = (float)j / n * Tau;
            if (!keep(ang)) continue;
            float ca = MathF.Cos(ang), sa = MathF.Sin(ang);
            for (float off = -thick / 2; off <= thick / 2; off += 0.5f)
                buf.Dot(cx + ca * (rx + off), cy + sa * (ry + off * (ry / MathF.Max(1, rx))), c, a);
        }
    }
}
