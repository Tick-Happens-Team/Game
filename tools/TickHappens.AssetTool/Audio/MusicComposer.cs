using System.Text.Json;

namespace TickHappens.AssetTool.Audio;

/// <summary>
/// Composes and renders a loop-seamless stereo track from a text recipe (<c>art/audio/music/*.music.json</c>).
/// The recipe fixes tempo, key, form, chord progressions and instruments; the seed generates the melodies
/// (motif, repetition, sequence on the next chord, cadence) so the same recipe always renders the same file.
/// Tones use PolyBLEP oscillators with ADSR and vibrato; drums are <see cref="SoundSynth"/> layers; a ping-pong
/// echo and a small Schroeder reverb give the 16-bit console space. The tail is folded onto the start, so the
/// file loops without a seam.
/// </summary>
public static class MusicComposer
{
    private const int Rate = SoundSynth.Rate, Steps = 16;
    private static readonly string[] Waves = ["sine", "saw", "square", "triangle", "pulse"];

    private sealed class Track
    {
        public string Name = "", Role = "lead";
        public int Wave, Octave = 4, Index;
        public float Duty = 0.5f, Gain = 0.2f, Pan, Attack = 0.005f, Decay = 0.15f, Sustain = 0.6f, Release = 0.06f;
        public float Lowpass, VibRate, VibDepth, EchoSend, ReverbSend, Density = 0.6f, Detune;
        public int Lo, Hi = 11;
        public string Pattern = "";
        public string? Fill;
        public Dictionary<string, string> Patterns = [];
        public HashSet<string>? Sections;
        public float[]? Hit;
    }

    private sealed class Section
    {
        public int Bars = 4;
        public int[] Chords = [0];
    }

    private readonly record struct Note(double Start, double Length, int Midi, float Velocity);

    public sealed class Result
    {
        public string Name = "";
        public float[] Stereo = [];
        public double Seconds;
        public int Bars;
    }

    public static Result Compose(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var j = doc.RootElement;
        Json.CheckKeys(j, file, "name", "_comment", "seed", "bpm", "root", "scale", "swing", "sections", "form", "tracks", "echo", "reverb", "peak");
        string name = Json.Str(j, "name", Path.GetFileName(file).Split('.')[0]);
        int seed = Json.Int(j, "seed", 1), root = Json.Int(j, "root", 57);
        float bpm = Json.Float(j, "bpm", 120), swing = Json.Float(j, "swing", 0), peak = Json.Float(j, "peak", 0.85f);
        int[] scale = j.TryGetProperty("scale", out var sc) ? [.. sc.EnumerateArray().Select(x => x.GetInt32())] : [0, 2, 3, 5, 7, 8, 10];

        var sections = new Dictionary<string, Section>();
        foreach (var p in j.GetProperty("sections").EnumerateObject())
        {
            Json.CheckKeys(p.Value, $"{file}: sections.{p.Name}", "bars", "chords", "_comment");
            sections[p.Name] = new Section
            {
                Bars = Json.Int(p.Value, "bars", 4),
                Chords = p.Value.TryGetProperty("chords", out var c) ? [.. c.EnumerateArray().Select(x => x.GetInt32())] : [0],
            };
        }
        string[] form = [.. j.GetProperty("form").EnumerateArray().Select(x => x.GetString()!)];
        foreach (var s in form)
            if (!sections.ContainsKey(s)) throw new InvalidDataException($"{file}: form names unknown section '{s}'");

        var tracks = new List<Track>();
        foreach (var t in j.GetProperty("tracks").EnumerateArray()) tracks.Add(ParseTrack(t, file, tracks.Count));

        double step = 60.0 / bpm / 4;
        int totalBars = form.Sum(s => sections[s].Bars);
        int n = (int)Math.Round(totalBars * Steps * step * Rate), tail = 4 * Rate;

        var outL = new float[n + tail];
        var outR = new float[n + tail];
        var echoBus = new float[n + tail];
        var reverbBus = new float[n + tail];
        var mono = new float[n + tail];

        foreach (var track in tracks)
        {
            Array.Clear(mono);
            var notes = Arrange(track, form, sections, scale, root, seed, step, swing);
            if (track.Role == "drum")
            {
                foreach (var note in notes)
                {
                    int at = (int)(note.Start * Rate);
                    for (int i = 0; i < track.Hit!.Length && at + i < mono.Length; i++) mono[at + i] += track.Hit[i] * note.Velocity;
                }
            }
            else foreach (var note in notes) RenderNote(mono, track, note);
            if (track.Lowpass > 0)
            {
                Biquad lp = default;
                lp.Set(false, track.Lowpass, Rate);
                for (int i = 0; i < mono.Length; i++) mono[i] = lp.Run(mono[i]);
            }
            float angle = (Math.Clamp(track.Pan, -1, 1) + 1) * MathF.PI / 4, gl = MathF.Cos(angle) * 1.414f * track.Gain, gr = MathF.Sin(angle) * 1.414f * track.Gain;
            for (int i = 0; i < mono.Length; i++)
            {
                float x = mono[i];
                outL[i] += x * gl;
                outR[i] += x * gr;
                echoBus[i] += x * track.Gain * track.EchoSend;
                reverbBus[i] += x * track.Gain * track.ReverbSend;
            }
        }

        if (j.TryGetProperty("echo", out var e))
        {
            Json.CheckKeys(e, file + ": echo", "beats", "feedback", "mix", "damp");
            PingPong(echoBus, outL, outR, (int)(Json.Float(e, "beats", 0.75f) * 60 / bpm * Rate),
                Json.Float(e, "feedback", 0.35f), Json.Float(e, "mix", 0.5f), Json.Float(e, "damp", 3500));
        }
        if (j.TryGetProperty("reverb", out var r))
        {
            Json.CheckKeys(r, file + ": reverb", "mix", "size", "damp");
            Reverb(reverbBus, outL, outR, Json.Float(r, "mix", 0.3f), Json.Float(r, "size", 0.8f), Json.Float(r, "damp", 0.35f));
        }

        // Fold the tail onto the start: whatever rings past the loop point is heard at the top of the next pass.
        var stereo = new float[n * 2];
        for (int i = 0; i < n + tail; i++)
        {
            int k = i % n;
            stereo[k * 2] += outL[i];
            stereo[k * 2 + 1] += outR[i];
        }
        float m = 0;
        for (int i = 0; i < stereo.Length; i++) m = MathF.Max(m, MathF.Abs(stereo[i]));
        // Gentle glue: normalize to the peak, then soft-saturate the loudest transients.
        for (int i = 0; i < stereo.Length; i++)
        {
            float x = stereo[i] / MathF.Max(m, 1e-6f) * 1.15f;
            stereo[i] = MathF.Tanh(x) / MathF.Tanh(1.15f) * peak;
        }
        return new Result { Name = name, Stereo = stereo, Seconds = (double)n / Rate, Bars = totalBars };
    }

    private static Track ParseTrack(JsonElement t, string file, int index)
    {
        string where = $"{file}: tracks[{index}]";
        Json.CheckKeys(t, where, "name", "_comment", "role", "wave", "duty", "octave", "gain", "pan", "attack", "decay", "sustain", "release",
            "lowpass", "vibrato", "echo", "reverb", "density", "range", "pattern", "patterns", "fill", "sections", "layers", "detune");
        var track = new Track
        {
            Index = index,
            Name = Json.Str(t, "name", $"track{index}"),
            Role = Json.Str(t, "role", "lead"),
            Duty = Json.Float(t, "duty", 0.5f),
            Octave = Json.Int(t, "octave", 4),
            Gain = Json.Float(t, "gain", 0.2f),
            Pan = Json.Float(t, "pan", 0),
            Attack = Json.Float(t, "attack", 5) / 1000f,
            Decay = Json.Float(t, "decay", 150) / 1000f,
            Sustain = Json.Float(t, "sustain", 0.6f),
            Release = Json.Float(t, "release", 60) / 1000f,
            Lowpass = Json.Float(t, "lowpass", 0),
            EchoSend = Json.Float(t, "echo", 0),
            ReverbSend = Json.Float(t, "reverb", 0),
            Density = Json.Float(t, "density", 0.6f),
            Detune = Json.Float(t, "detune", 0),
            Pattern = Json.Str(t, "pattern", ""),
            Fill = t.TryGetProperty("fill", out var f) ? f.GetString() : null,
        };
        if (track.Role is not ("lead" or "bass" or "arp" or "pad" or "drum"))
            throw new InvalidDataException($"{where}.role: expected lead, bass, arp, pad or drum");
        track.Wave = Array.IndexOf(Waves, Json.Str(t, "wave", "square"));
        if (track.Wave < 0) throw new InvalidDataException($"{where}.wave: expected one of {string.Join(", ", Waves)}");
        if (t.TryGetProperty("vibrato", out var v)) { track.VibRate = v[0].GetSingle(); track.VibDepth = v[1].GetSingle(); }
        if (t.TryGetProperty("range", out var rg)) { track.Lo = rg[0].GetInt32(); track.Hi = rg[1].GetInt32(); }
        if (t.TryGetProperty("patterns", out var ps)) foreach (var p in ps.EnumerateObject()) track.Patterns[p.Name] = p.Value.GetString()!;
        if (t.TryGetProperty("sections", out var ss)) track.Sections = [.. ss.EnumerateArray().Select(x => x.GetString()!)];
        if (track.Role == "drum")
        {
            var layers = SoundSynth.ParseLayers(t, where);
            float len = 0.02f;
            foreach (var l in layers) len = MathF.Max(len, l.Start + l.Attack + l.Hold + l.Decay * 7);
            track.Hit = new float[(int)(MathF.Min(len, 2) * Rate)];
            uint noise = (uint)(index * 7919 + 17);
            foreach (var l in layers) SoundSynth.RenderLayer(track.Hit, l, noise = noise * 1664525u + 1013904223u | 1u);
        }
        else if (track.Role != "lead" && track.Pattern.Length == 0 && track.Patterns.Count == 0)
            throw new InvalidDataException($"{where}: {track.Role} needs a pattern");
        return track;
    }

    private static int Midi(int degree, int[] scale, int root, int octave)
    {
        int len = scale.Length, o = (int)Math.Floor(degree / (double)len);
        return root + (octave - 4) * 12 + scale[degree - o * len] + 12 * o;
    }

    private static List<Note> Arrange(Track t, string[] form, Dictionary<string, Section> sections, int[] scale, int root,
        int seed, double step, float swing)
    {
        var notes = new List<Note>();
        var melodies = new Dictionary<string, List<(int Step, int Len, int Degree, float Vel)>>();
        int bar0 = 0;
        foreach (string name in form)
        {
            var sec = sections[name];
            if (t.Sections == null || t.Sections.Contains(name))
            {
                if (t.Role == "lead")
                {
                    if (!melodies.TryGetValue(name, out var mel))
                        melodies[name] = mel = Melody(t, sec, scale.Length, Hash(seed, t.Index, name));
                    foreach (var (s, len, deg, vel) in mel)
                        notes.Add(new Note(Time(bar0 * Steps + s, step, swing), len * step, Midi(deg, scale, root, t.Octave), vel));
                }
                else
                {
                    string pattern = t.Patterns.TryGetValue(name, out var p) ? p : t.Pattern;
                    for (int b = 0; b < sec.Bars; b++)
                    {
                        string bar = b == sec.Bars - 1 && t.Fill != null ? t.Fill : pattern;
                        int chord = sec.Chords[b % sec.Chords.Length];
                        // A pattern is one or more 16-step bars; shorter patterns repeat within the bar.
                        var cur = new char[Steps];
                        int slices = Math.Max(1, bar.Length / Steps);
                        for (int s = 0; s < Steps; s++)
                            cur[s] = bar.Length >= Steps ? bar[(b % slices) * Steps + s] : bar[s % bar.Length];
                        for (int s = 0; s < Steps; s++)
                        {
                            char c = cur[s];
                            if (c is '.' or '-') continue;
                            int len = 1;
                            while (s + len < Steps && cur[s + len] == '-') len++;
                            double at = Time((bar0 + b) * Steps + s, step, swing);
                            switch (t.Role)
                            {
                                case "drum":
                                    notes.Add(new Note(at, 0, 0, c switch { 'X' => 1f, 'g' => 0.35f, _ => 0.75f }));
                                    break;
                                case "bass":
                                    int d = c switch { 'o' => chord + scale.Length, '5' => chord + 4, '3' => chord + 2, '7' => chord + 6, _ => chord };
                                    notes.Add(new Note(at, len * step, Midi(d, scale, root, t.Octave), s % 4 == 0 ? 1f : 0.8f));
                                    break;
                                case "arp":
                                    int k = c is >= '0' and <= '9' ? c - '0' : s % 3;
                                    notes.Add(new Note(at, len * step, Midi(chord + (k % 3) * 2 + (k / 3) * scale.Length, scale, root, t.Octave), s % 4 == 0 ? 1f : 0.7f));
                                    break;
                                case "pad":
                                    for (int v = 0; v < 3; v++)
                                        notes.Add(new Note(at, len * step, Midi(chord + v * 2, scale, root, t.Octave), 0.8f));
                                    break;
                            }
                        }
                    }
                }
            }
            bar0 += sec.Bars;
        }
        return notes;
    }

    private static double Time(int globalStep, double step, float swing) => (globalStep + (globalStep % 2 == 1 ? swing : 0)) * step;

    private static ulong Hash(int seed, int track, string section)
    {
        ulong h = 1469598103934665603ul ^ (ulong)seed * 0x100000001B3ul ^ (ulong)track * 0x9E3779B97F4A7C15ul;
        foreach (char c in section) h = (h ^ c) * 1099511628211ul;
        return h;
    }

    /// <summary>
    /// Phrases of four bars: motif, motif sequenced onto the next chord, a contrasting bar, and the motif cut short
    /// into a cadence on the chord root. Eight-bar sections answer the first phrase with a new second half.
    /// </summary>
    private static List<(int, int, int, float)> Melody(Track t, Section sec, int scaleLen, ulong seed)
    {
        var rng = new SoundSynth.Rng(seed);
        var result = new List<(int, int, int, float)>();
        List<int> motifRhythm = Rhythm(ref rng, t.Density), contrast = Rhythm(ref rng, t.Density);
        int[]? motifShape = null;
        int degree = (t.Lo + t.Hi) / 2;
        for (int b = 0; b < sec.Bars; b++)
        {
            int chord = sec.Chords[b % sec.Chords.Length];
            int role = b % 4;
            if (b == 4) contrast = Rhythm(ref rng, t.Density);  // the answer phrase
            var rhythm = role == 2 ? contrast : motifRhythm;
            if (role == 3) rhythm = [.. motifRhythm.Where(s => s < 8), 8];
            var degrees = new int[rhythm.Count];
            for (int i = 0; i < rhythm.Count; i++)
            {
                int s = rhythm[i];
                if (role == 1 && motifShape != null && i < motifShape.Length) degree = chord + motifShape[i];
                else if (role == 3 && s == 8) degree = Nearest(degree, chord, scaleLen, 0);
                else
                {
                    int r = rng.Int(10);
                    degree += r < 3 ? 1 : r < 6 ? -1 : r < 8 ? 2 * (rng.Int(2) * 2 - 1) : r < 9 ? 0 : 4 * (rng.Int(2) * 2 - 1);
                    if (s % 4 == 0) degree = Nearest(degree, chord, scaleLen, -1);
                }
                while (degree > t.Hi) degree -= degree - t.Hi > 3 ? scaleLen : 2;
                while (degree < t.Lo) degree += t.Lo - degree > 3 ? scaleLen : 2;
                degrees[i] = degree;
            }
            if (role == 0) motifShape = [.. degrees.Select(d => d - chord)];
            for (int i = 0; i < rhythm.Count; i++)
            {
                int s = rhythm[i], end = i + 1 < rhythm.Count ? rhythm[i + 1] : Steps;
                int len = role == 3 && s == 8 ? 8 : Math.Min(end - s, 4);
                result.Add((b * Steps + s, len, degrees[i], s % 4 == 0 ? 1f : 0.78f));
            }
        }
        return result;
    }

    private static List<int> Rhythm(ref SoundSynth.Rng rng, float density)
    {
        var steps = new List<int>();
        for (int s = 0; s < Steps; s++)
        {
            float p = s % 4 == 0 ? 0.9f : s % 2 == 0 ? 0.55f : 0.18f;
            if (s == 0 || rng.Float() < p * density * 1.2f) steps.Add(s);
        }
        return steps;
    }

    /// <summary>Snaps a degree to the nearest chord tone (root, third, fifth); <paramref name="index"/> ≥ 0 forces that tone.</summary>
    private static int Nearest(int degree, int chord, int scaleLen, int index)
    {
        int best = degree, bestDist = int.MaxValue;
        for (int o = -3; o <= 3; o++)
            for (int k = 0; k < 3; k++)
            {
                if (index >= 0 && k != index) continue;
                int d = chord + k * 2 + o * scaleLen, dist = Math.Abs(d - degree);
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
        return best;
    }

    private static void RenderNote(float[] dst, Track t, in Note note)
    {
        int start = (int)(note.Start * Rate);
        double gate = Math.Max(note.Length * 0.92, t.Attack + 0.01);
        int len = (int)((gate + t.Release) * Rate);
        double freq = 440 * Math.Pow(2, (note.Midi - 69) / 12.0);
        double phase = 0, phase2 = 0.37;
        float detune = MathF.Pow(2, t.Detune / 1200f);
        for (int i = 0; i < len && start + i < dst.Length; i++)
        {
            float time = (float)i / Rate, env;
            if (time < t.Attack) env = time / t.Attack;
            else if (time < gate) env = t.Sustain + (1 - t.Sustain) * MathF.Exp(-(time - t.Attack) / MathF.Max(t.Decay, 0.001f));
            else
            {
                float atGate = t.Sustain + (1 - t.Sustain) * MathF.Exp(-((float)gate - t.Attack) / MathF.Max(t.Decay, 0.001f));
                env = atGate * MathF.Max(0, 1 - (time - (float)gate) / MathF.Max(t.Release, 0.001f));
            }
            double f = freq;
            if (t.VibDepth > 0 && time > 0.12f)
                f *= Math.Pow(2, t.VibDepth / 12 * Math.Sin(2 * Math.PI * t.VibRate * time) * Math.Min(1, (time - 0.12f) * 4));
            float dt = (float)(f / Rate);
            float x = Sample(t, phase, dt);
            if (t.Detune != 0) x = 0.6f * x + 0.4f * Sample(t, phase2, dt * detune);
            dst[start + i] += x * env * note.Velocity;
            phase += dt;
            phase2 += dt * detune;
            if (phase >= 1) phase -= 1;
            if (phase2 >= 1) phase2 -= 1;
        }
    }

    private static float Sample(Track t, double phase, float dt)
    {
        float u = (float)phase;
        switch (t.Wave)
        {
            case 0: return MathF.Sin(u * MathF.Tau);
            case 1: return 2 * u - 1 - Osc.Blep(u, dt);
            case 2:
            case 4:
            {
                float duty = t.Wave == 2 ? 0.5f : t.Duty, v = u < duty ? 1 : -1;
                float w = u - duty;
                if (w < 0) w += 1;
                return v + Osc.Blep(u, dt) - Osc.Blep(w, dt) - (2 * duty - 1);
            }
            default:
                // 32-step triangle: the console wavetable bass.
                return MathF.Round((1 - 4 * MathF.Abs(u - 0.5f)) * 8) / 8;
        }
    }

    private static void PingPong(float[] send, float[] l, float[] r, int delay, float feedback, float mix, float damp)
    {
        delay = Math.Max(1, delay);
        var dl = new float[send.Length];
        var dr = new float[send.Length];
        Biquad a = default, b = default;
        a.Set(false, damp, Rate);
        b.Set(false, damp, Rate);
        for (int i = 0; i < send.Length; i++)
        {
            float yl = i >= delay ? dl[i - delay] : 0, yr = i >= delay ? dr[i - delay] : 0;
            dl[i] = send[i] + a.Run(yr) * feedback;
            dr[i] = b.Run(yl) * feedback;
            l[i] += yl * mix;
            r[i] += yr * mix;
        }
    }

    private static void Reverb(float[] send, float[] l, float[] r, float mix, float size, float damp)
    {
        int[] combs = [1116, 1188, 1277, 1356], allpasses = [556, 441];
        for (int ch = 0; ch < 2; ch++)
        {
            var output = ch == 0 ? l : r;
            var wet = new float[send.Length];
            foreach (int baseLen in combs)
            {
                int len = (int)((baseLen + ch * 23) * Rate / 44100f);
                var buf = new float[len];
                float store = 0, fb = 0.7f + 0.28f * Math.Clamp(size, 0, 1);
                for (int i = 0, k = 0; i < send.Length; i++, k = k + 1 == len ? 0 : k + 1)
                {
                    float y = buf[k];
                    store = y * (1 - damp) + store * damp;
                    buf[k] = send[i] + store * fb;
                    wet[i] += y * 0.25f;
                }
            }
            foreach (int baseLen in allpasses)
            {
                int len = (int)((baseLen + ch * 23) * Rate / 44100f);
                var buf = new float[len];
                for (int i = 0, k = 0; i < wet.Length; i++, k = k + 1 == len ? 0 : k + 1)
                {
                    float y = buf[k], x = wet[i];
                    buf[k] = x + y * 0.5f;
                    wet[i] = y - x;
                }
            }
            for (int i = 0; i < output.Length; i++) output[i] += wet[i] * mix;
        }
    }
}
