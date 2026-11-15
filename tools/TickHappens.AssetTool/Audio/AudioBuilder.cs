using TickHappens.Engine.Assets;
using TickHappens.Engine.Audio;

namespace TickHappens.AssetTool.Audio;

/// <summary>
/// Renders every sound and music recipe in Assets into runtime WAV files (16-bit PCM, <see cref="AudioMixer.Rate"/> Hz):
/// <c>art/audio/sfx/NAME.sound.json</c> → <c>assets/audio/sfx/NAME.V.wav</c> (one file per variant) and
/// <c>art/audio/music/NAME.music.json</c> → <c>assets/audio/music/NAME.wav</c> (stereo loop). Waveform previews go
/// to <c>.tmp/preview/audio</c>.
/// </summary>
public static class AudioBuilder
{
    public static int Build(string assetsRoot, string previewDir, string? only = null)
    {
        string src = Path.Combine(assetsRoot, "art", "audio"), dst = Path.Combine(assetsRoot, "assets", "audio");
        if (!Directory.Exists(src)) return 0;
        string sfxOut = Path.Combine(dst, "sfx"), musicOut = Path.Combine(dst, "music"), preview = Path.Combine(previewDir, "audio");
        Directory.CreateDirectory(sfxOut);
        Directory.CreateDirectory(musicOut);
        int files = 0;
        foreach (var f in Recipes(Path.Combine(src, "sfx"), "*.sound.json", only))
        {
            var r = SoundSynth.Generate(f);
            foreach (var old in Directory.EnumerateFiles(sfxOut, r.Name + ".*.wav")) File.Delete(old);
            for (int v = 0; v < r.Variants.Count; v++)
            {
                WavFile.Write(Path.Combine(sfxOut, $"{r.Name}.{v + 1}.wav"), r.Variants[v], 1, SoundSynth.Rate);
                files++;
            }
            Waveform(r.Variants, Path.Combine(preview, r.Name + ".png"));
            Console.WriteLine($"sfx   {r.Name,-16} {r.Variants.Count} variants, {r.Variants.Max(v => v.Length) * 1000 / SoundSynth.Rate} ms");
        }
        foreach (var f in Recipes(Path.Combine(src, "music"), "*.music.json", only))
        {
            var r = MusicComposer.Compose(f);
            WavFile.Write(Path.Combine(musicOut, r.Name + ".wav"), r.Stereo, 2, SoundSynth.Rate);
            files++;
            var left = new float[r.Stereo.Length / 2];
            for (int i = 0; i < left.Length; i++) left[i] = (r.Stereo[i * 2] + r.Stereo[i * 2 + 1]) * 0.5f;
            Waveform([left], Path.Combine(preview, r.Name + ".png"));
            Console.WriteLine($"music {r.Name,-16} {r.Bars} bars, {r.Seconds:F1} s loop, RMS {Rms(r.Stereo):F3}");
        }
        Console.WriteLine($"audio: {files} files -> {dst}");
        return files;
    }

    private static IEnumerable<string> Recipes(string dir, string pattern, string? only) =>
        !Directory.Exists(dir) ? [] : Directory.EnumerateFiles(dir, pattern).Order()
            .Where(f => only == null || Path.GetFileName(f).StartsWith(only + ".", StringComparison.Ordinal));

    public static float Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;
        foreach (float x in s) sum += x * x;
        return (float)Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    /// <summary>One row per clip: min/max envelope in blue over a dark ground, a center line, 0.1 s ticks.</summary>
    private static void Waveform(IReadOnlyList<float[]> clips, string path)
    {
        const int W = 640, H = 64;
        var img = new RgbaImage(W, H * clips.Count);
        for (int c = 0; c < clips.Count; c++)
        {
            var s = clips[c];
            int y0 = c * H;
            for (int x = 0; x < W; x++)
            {
                int a = (int)((long)s.Length * x / W), b = Math.Max(a + 1, (int)((long)s.Length * (x + 1) / W));
                float lo = 0, hi = 0;
                for (int i = a; i < b && i < s.Length; i++) { lo = MathF.Min(lo, s[i]); hi = MathF.Max(hi, s[i]); }
                for (int y = 0; y < H; y++)
                {
                    float v = 1 - 2 * (y + 0.5f) / H;
                    bool tick = (int)((long)s.Length * x / W / (SoundSynth.Rate / 10)) != (int)((long)s.Length * Math.Max(0, x - 1) / W / (SoundSynth.Rate / 10));
                    img[x, y0 + y] = v >= lo && v <= hi ? RgbaImage.Pack(110, 200, 255)
                        : y == H / 2 || tick ? RgbaImage.Pack(50, 50, 70) : RgbaImage.Pack(20, 18, 30);
                }
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        img.Save(path);
    }
}
