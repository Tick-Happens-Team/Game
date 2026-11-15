using System.Text.Json;
using TickHappens.Engine.Audio;

namespace TickHappens.AssetTool.Audio;

/// <summary>One synthesized layer: filtered noise or a pitch-swept tone with an attack / hold / decay envelope.</summary>
public struct SynthLayer
{
    public bool Tone;
    /// <summary>Seconds.</summary>
    public float Start, Attack, Hold, Decay;
    public float Gain, Lowpass, Highpass, F0, F1, Drive;
    /// <summary>0 sine, 1 saw, 2 square, 3 triangle.</summary>
    public int Wave;
}

/// <summary>
/// Procedural sound effects from text recipes (<c>art/audio/sfx/*.sound.json</c>): layers of filtered noise and
/// pitch-swept tones, a small room echo, optional sample-and-hold grit and N jittered variants. Deterministic by
/// seed. The layer model and recipe keys follow the author's Nelomoria.NET SoundSynth, adapted to this pipeline.
/// </summary>
public static class SoundSynth
{
    public const int Rate = AudioMixer.Rate;
    private static readonly string[] Waves = ["sine", "saw", "square", "triangle"];

    public sealed class Result
    {
        public string Name = "";
        public List<float[]> Variants { get; } = [];
    }

    /// <summary>xorshift64* with a float range helper; independent from System.Random so output never changes with the runtime.</summary>
    public struct Rng(ulong seed)
    {
        private ulong _s = seed * 0x9E3779B97F4A7C15ul | 1;

        public uint Next()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            return (uint)((_s * 0x2545F4914F6CDD1Dul) >> 32);
        }

        public float Float() => (Next() >> 8) / 16777216f;
        public float Range(float a, float b) => a + (b - a) * Float();
        public int Int(int n) => (int)(Next() % (uint)n);
    }

    public static Result Generate(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var j = doc.RootElement;
        Json.CheckKeys(j, file, "name", "_comment", "variants", "seed", "vary", "peak", "layers", "echo", "lofi", "length");
        var result = new Result { Name = Json.Str(j, "name", Path.GetFileName(file).Split('.')[0]) };
        int variants = Math.Clamp(Json.Int(j, "variants", 4), 1, 16);
        var rng = new Rng((ulong)Json.Int(j, "seed", 1));
        float vary = Json.Float(j, "vary", 0.1f), peak = Json.Float(j, "peak", 0.8f);
        var bases = ParseLayers(j, file);
        float echoDelay = 0, echoFb = 0, echoMix = 0, echoDamp = 4000;
        if (j.TryGetProperty("echo", out var e))
        {
            Json.CheckKeys(e, file + ": echo", "delay", "feedback", "mix", "damp");
            echoDelay = Json.Float(e, "delay", 60) / 1000f;
            echoFb = Math.Clamp(Json.Float(e, "feedback", 0.3f), 0, 0.9f);
            echoMix = Json.Float(e, "mix", 0.2f);
            echoDamp = Json.Float(e, "damp", 4000);
        }
        float lofi = Json.Float(j, "lofi", 0), fixedLen = Json.Float(j, "length", 0) / 1000f;

        var ls = new SynthLayer[bases.Length];
        for (int v = 0; v < variants; v++)
        {
            float pitch = 1 + rng.Range(-vary, vary), timing = 1 + rng.Range(-vary, vary);
            for (int i = 0; i < ls.Length; i++)
            {
                var l = bases[i];
                l.Gain *= 1 + rng.Range(-vary, vary) * 1.5f;
                l.Decay *= 1 + rng.Range(-vary, vary);
                l.Start *= timing;
                l.F0 *= pitch;
                l.F1 *= pitch;
                if (l.Lowpass > 0) l.Lowpass *= pitch * (1 + rng.Range(-vary, vary));
                if (l.Highpass > 0) l.Highpass *= pitch;
                ls[i] = l;
            }
            float len = 0.02f;
            foreach (var l in ls) len = MathF.Max(len, l.Start + l.Attack + l.Hold + l.Decay * 7);
            if (echoMix > 0) len += echoDelay * MathF.Log(0.001f) / MathF.Log(MathF.Max(echoFb, 0.01f));
            len = fixedLen > 0 ? MathF.Min(fixedLen, 30) : MathF.Min(len, 3);
            var s = new float[(int)(len * Rate)];
            foreach (var l in ls) RenderLayer(s, l, rng.Next() | 1u);
            if (echoMix > 0) Echo(s, echoDelay, echoFb, echoMix, echoDamp);
            if (lofi > 0) SampleHold(s, lofi);
            Normalize(s, peak);
            if (fixedLen <= 0)
            {
                // Drop the inaudible tail (below -60 dB of the peak).
                int end = s.Length;
                while (end > 64 && MathF.Abs(s[end - 1]) < peak * 0.001f) end--;
                s = s[..Math.Min(s.Length, end + Rate / 100)];
            }
            for (int i = 0; i < 64 && i < s.Length; i++) s[^(i + 1)] *= i / 64f;  // no click at the end
            result.Variants.Add(s);
        }
        return result;
    }

    public static SynthLayer[] ParseLayers(JsonElement j, string where)
    {
        if (!j.TryGetProperty("layers", out var arr)) return [];
        var list = new List<SynthLayer>();
        int index = 0;
        foreach (var lj in arr.EnumerateArray())
        {
            string p = $"{where}: layers[{index++}]";
            Json.CheckKeys(lj, p, "type", "start", "attack", "hold", "decay", "gain", "lowpass", "highpass", "freq", "wave", "drive", "_comment");
            string type = Json.Str(lj, "type", "noise");
            if (type is not ("noise" or "tone")) throw new InvalidDataException($"{p}.type: expected noise or tone");
            int wave = Array.IndexOf(Waves, Json.Str(lj, "wave", "sine"));
            if (wave < 0) throw new InvalidDataException($"{p}.wave: expected sine, saw, square or triangle");
            float f0 = 200, f1 = 150;
            if (lj.TryGetProperty("freq", out var f)) { f0 = f[0].GetSingle(); f1 = f[1].GetSingle(); }
            list.Add(new SynthLayer
            {
                Tone = type == "tone",
                Start = Json.Float(lj, "start", 0) / 1000f,
                Attack = MathF.Max(Json.Float(lj, "attack", 1) / 1000f, 0.0002f),
                Hold = MathF.Max(Json.Float(lj, "hold", 0) / 1000f, 0),
                Decay = MathF.Max(Json.Float(lj, "decay", 30) / 1000f, 0.0005f),
                Gain = Json.Float(lj, "gain", 0.5f),
                Lowpass = Json.Float(lj, "lowpass", 0),
                Highpass = Json.Float(lj, "highpass", 0),
                F0 = f0, F1 = f1, Wave = wave,
                Drive = MathF.Max(Json.Float(lj, "drive", 0), 0),
            });
        }
        return [.. list];
    }

    private static float Envelope(in SynthLayer l, float t)
    {
        t -= l.Start;
        if (t < 0) return 0;
        if (t < l.Attack) return t / l.Attack;
        if (t < l.Attack + l.Hold) return 1;
        return MathF.Exp(-(t - l.Attack - l.Hold) / l.Decay);
    }

    /// <summary>Adds one layer into <paramref name="s"/> (mono, <see cref="Rate"/>).</summary>
    public static void RenderLayer(Span<float> s, in SynthLayer l, uint noise)
    {
        Biquad lp = default, hp = default;
        if (l.Lowpass > 0) lp.Set(false, l.Lowpass, Rate);
        if (l.Highpass > 0) hp.Set(true, l.Highpass, Rate);
        double phase = 0;
        int n = s.Length;
        int i0 = (int)Math.Min(n, (long)(MathF.Max(l.Start, 0) * Rate));
        int i1 = (int)Math.Min(n, (long)((l.Start + l.Attack + l.Hold + l.Decay * 12) * Rate) + 1);
        float drive = l.Drive, norm = drive > 0 ? 1 / MathF.Tanh(drive) : 1;
        for (int i = i0; i < i1; i++)
        {
            float t = (float)i / Rate, e = Envelope(l, t), x;
            if (l.Tone)  // exponential sweep over ~3 decays
            {
                float k = Math.Clamp((t - l.Start) / (l.Decay * 3), 0f, 1f);
                phase += 2.0 * Math.PI * (l.F0 * MathF.Pow(l.F1 / l.F0, k)) / Rate;
                x = l.Wave == 0 ? (float)Math.Sin(phase) : Osc.Naive(l.Wave, phase / (2 * Math.PI));
                if (drive > 0) x = MathF.Tanh(x * drive) * norm;
                if (l.Highpass > 0) x = hp.Run(x);
                if (l.Lowpass > 0) x = lp.Run(x);
            }
            else
            {
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                x = (float)noise / 2147483648.0f - 1;
                if (l.Highpass > 0) x = hp.Run(x);
                if (l.Lowpass > 0) x = lp.Run(x);
                if (drive > 0) x = MathF.Tanh(x * drive) * norm;
            }
            s[i] += x * e * l.Gain;
        }
    }

    /// <summary>A room: two damped feedback delays mixed in as a wet signal.</summary>
    public static void Echo(float[] s, float delay, float feedback, float mix, float damp)
    {
        int n = s.Length, d1 = Math.Max(1, (int)(delay * Rate)), d2 = Math.Max(1, (int)(delay * 1.37f * Rate));
        Biquad a = default, b = default;
        a.Set(false, damp, Rate);
        b.Set(false, damp * 0.8f, Rate);
        var c1 = new float[n];
        var c2 = new float[n];
        for (int i = 0; i < n; i++)
        {
            c1[i] = s[i] + (i >= d1 ? a.Run(c1[i - d1]) * feedback : 0);
            c2[i] = s[i] + (i >= d2 ? b.Run(c2[i - d2]) * feedback : 0);
        }
        for (int i = 0; i < n; i++) s[i] += ((c1[i] + c2[i]) * 0.5f - s[i]) * mix;
    }

    private static void SampleHold(float[] s, float rate)
    {
        float step = Rate / rate, acc = 0, held = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if ((acc += 1) >= step) { acc -= step; held = s[i]; }
            s[i] = held;
        }
    }

    public static void Normalize(Span<float> s, float peak)
    {
        float m = 0;
        foreach (float x in s) m = MathF.Max(m, MathF.Abs(x));
        if (m > 0) for (int i = 0; i < s.Length; i++) s[i] *= peak / m;
    }
}

/// <summary>RBJ biquad lowpass / highpass.</summary>
public struct Biquad
{
    private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

    public void Set(bool high, float freq, int rate, float q = 0.707f)
    {
        float w = 2 * MathF.PI * Math.Clamp(freq, 10f, rate * 0.45f) / rate, c = MathF.Cos(w), al = MathF.Sin(w) / (2 * q), a0 = 1 + al;
        float b = high ? (1 + c) / 2 : (1 - c) / 2;
        _b0 = b / a0;
        _b1 = (high ? -(1 + c) : 1 - c) / a0;
        _b2 = b / a0;
        _a1 = -2 * c / a0;
        _a2 = (1 - al) / a0;
    }

    public float Run(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>Oscillator shapes. <c>t</c> is the phase in cycles.</summary>
public static class Osc
{
    public static float Naive(int wave, double t)
    {
        float u = (float)(t - Math.Floor(t));
        return wave switch
        {
            1 => 2 * u - 1,
            2 => u < 0.5f ? 1 : -1,
            _ => 1 - 4 * MathF.Abs(u - 0.5f),
        };
    }

    /// <summary>PolyBLEP correction for a discontinuity at phase 0 (dt = cycles per sample).</summary>
    public static float Blep(float t, float dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }
}

internal static class Json
{
    public static void CheckKeys(JsonElement j, string where, params string[] keys)
    {
        if (j.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{where}: expected an object");
        foreach (var p in j.EnumerateObject())
            if (Array.IndexOf(keys, p.Name) < 0) throw new InvalidDataException($"{where}: unknown key '{p.Name}'");
    }

    public static float Float(JsonElement j, string key, float def) => j.TryGetProperty(key, out var v) ? v.GetSingle() : def;
    public static int Int(JsonElement j, string key, int def) => j.TryGetProperty(key, out var v) ? v.GetInt32() : def;
    public static string Str(JsonElement j, string key, string def) => j.TryGetProperty(key, out var v) ? v.GetString() ?? def : def;
    public static bool Bool(JsonElement j, string key, bool def) => j.TryGetProperty(key, out var v) ? v.GetBoolean() : def;
}
