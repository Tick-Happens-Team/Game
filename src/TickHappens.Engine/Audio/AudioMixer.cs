namespace TickHappens.Engine.Audio;

/// <summary>
/// Software stereo mixer without a device: a fixed voice pool for effects with per-clip instance caps,
/// two looping music slots for crossfades, music and effect buses, and a peak limiter so any number of
/// simultaneous hits never clips. <see cref="AudioDevice"/> feeds its output to SDL; tests and capture runs
/// call <see cref="Mix"/> directly. Not thread-safe: call from one thread.
/// </summary>
public sealed class AudioMixer
{
    public const int Rate = 32000, MaxVoices = 48;
    private const float Ceiling = 0.92f;

    private struct Voice
    {
        public AudioClip? Clip;
        public double Pos;
        public float Step, GainL, GainR;
        public long Started;
    }

    private struct MusicSlot
    {
        public AudioClip? Clip;
        public double Pos;
        public float Gain, Target, Rate;
    }

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly MusicSlot[] _music = new MusicSlot[2];
    private long _serial;
    private float _limit = 1;

    public float MasterVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 0.55f;
    public float SfxVolume { get; set; } = 0.9f;
    /// <summary>Peak absolute sample written by the last <see cref="Mix"/> call (for tests and meters).</summary>
    public float LastPeak { get; private set; }
    public AudioClip? CurrentMusic => _music[0].Target > 0 ? _music[0].Clip : null;

    public int ActiveVoices
    {
        get
        {
            int n = 0;
            foreach (ref readonly var v in _voices.AsSpan()) if (v.Clip != null) n++;
            return n;
        }
    }

    /// <summary>Starts a one-shot effect. <paramref name="pan"/> is -1 (left) .. 1 (right). At most
    /// <paramref name="maxInstances"/> copies of one clip play at once; a new one replaces the oldest copy.</summary>
    public void Play(AudioClip clip, float volume = 1, float pitch = 1, float pan = 0, int maxInstances = 4)
    {
        if (volume <= 0.001f) return;
        int same = 0, oldestSame = -1, free = -1, oldest = 0;
        for (int i = 0; i < MaxVoices; i++)
        {
            ref var v = ref _voices[i];
            if (v.Clip == null) { if (free < 0) free = i; continue; }
            if (v.Started < _voices[oldest].Started || _voices[oldest].Clip == null) oldest = i;
            if (ReferenceEquals(v.Clip, clip))
            {
                same++;
                if (oldestSame < 0 || v.Started < _voices[oldestSame].Started) oldestSame = i;
            }
        }
        int slot = same >= Math.Max(1, maxInstances) ? oldestSame : free >= 0 ? free : oldest;
        pan = Math.Clamp(pan, -1, 1);
        float angle = (pan + 1) * MathF.PI / 4;  // equal power
        _voices[slot] = new Voice
        {
            Clip = clip, Pos = 0, Step = pitch * clip.Rate / Rate, Started = ++_serial,
            GainL = volume * MathF.Cos(angle) * 1.41421356f, GainR = volume * MathF.Sin(angle) * 1.41421356f,
        };
    }

    /// <summary>Crossfades to a looping track over <paramref name="fadeSeconds"/>. The same track keeps playing.</summary>
    public void PlayMusic(AudioClip clip, float volume = 1, float fadeSeconds = 1.5f)
    {
        if (ReferenceEquals(_music[0].Clip, clip) && _music[0].Target > 0)
        {
            _music[0].Target = volume;
            _music[0].Rate = volume / MathF.Max(0.01f, fadeSeconds) / Rate;
            return;
        }
        _music[1] = _music[0];
        _music[1].Target = 0;
        _music[1].Rate = MathF.Max(_music[1].Gain, 0.01f) / MathF.Max(0.01f, fadeSeconds) / Rate;
        _music[0] = new MusicSlot { Clip = clip, Pos = 0, Gain = 0, Target = volume, Rate = volume / MathF.Max(0.01f, fadeSeconds) / Rate };
    }

    /// <summary>Changes the current track's level (ducking) without restarting it.</summary>
    public void SetMusicLevel(float volume, float fadeSeconds = 0.5f)
    {
        if (_music[0].Clip == null) return;
        _music[0].Target = volume;
        _music[0].Rate = MathF.Max(0.0001f, MathF.Abs(volume - _music[0].Gain)) / MathF.Max(0.01f, fadeSeconds) / Rate;
    }

    public void StopMusic(float fadeSeconds = 1f) => SetMusicLevel(0, fadeSeconds);

    public void StopAll()
    {
        Array.Clear(_voices);
        Array.Clear(_music);
    }

    /// <summary>Mixes interleaved stereo frames into <paramref name="stereo"/> (overwrites it).</summary>
    public void Mix(Span<float> stereo)
    {
        stereo.Clear();
        int frames = stereo.Length / 2;
        float sfx = SfxVolume * MasterVolume, mus = MusicVolume * MasterVolume;
        for (int i = 0; i < MaxVoices; i++)
        {
            ref var v = ref _voices[i];
            if (v.Clip == null) continue;
            var s = v.Clip.Samples;
            int ch = v.Clip.Channels, n = v.Clip.Frames;
            float gl = v.GainL * sfx, gr = v.GainR * sfx;
            for (int f = 0; f < frames; f++)
            {
                int k = (int)v.Pos;
                if (k + 1 >= n) { v.Clip = null; break; }
                float t = (float)(v.Pos - k);
                float l, r;
                if (ch == 1) { l = r = s[k] + (s[k + 1] - s[k]) * t; }
                else
                {
                    l = s[k * 2] + (s[k * 2 + 2] - s[k * 2]) * t;
                    r = s[k * 2 + 1] + (s[k * 2 + 3] - s[k * 2 + 1]) * t;
                }
                stereo[f * 2] += l * gl;
                stereo[f * 2 + 1] += r * gr;
                v.Pos += v.Step;
            }
        }
        for (int m = 0; m < _music.Length; m++)
        {
            ref var slot = ref _music[m];
            if (slot.Clip == null) continue;
            var s = slot.Clip.Samples;
            int ch = slot.Clip.Channels, n = slot.Clip.Frames;
            double step = (double)slot.Clip.Rate / Rate;
            for (int f = 0; f < frames; f++)
            {
                if (slot.Gain < slot.Target) slot.Gain = MathF.Min(slot.Target, slot.Gain + slot.Rate);
                else if (slot.Gain > slot.Target) slot.Gain = MathF.Max(slot.Target, slot.Gain - slot.Rate);
                int k = (int)slot.Pos;
                float g = slot.Gain * mus;
                stereo[f * 2] += s[k * ch] * g;
                stereo[f * 2 + 1] += s[k * ch + ch - 1] * g;
                slot.Pos += step;
                if (slot.Pos >= n) slot.Pos -= n;  // loop: music files are rendered loop-seamless
            }
            if (slot.Gain <= 0 && slot.Target <= 0 && m == 1) slot.Clip = null;
        }
        // Limiter: instant attack, ~150 ms release, then a soft knee as a last guard.
        float release = 1f / (0.15f * Rate), peak = 0;
        for (int i = 0; i < stereo.Length; i += 2)
        {
            float a = MathF.Max(MathF.Abs(stereo[i]), MathF.Abs(stereo[i + 1]));
            if (a * _limit > Ceiling) _limit = Ceiling / a;
            else _limit = MathF.Min(1, _limit + release);
            for (int c = 0; c < 2; c++)
            {
                float x = stereo[i + c] * _limit;
                stereo[i + c] = x = MathF.Abs(x) < 0.8f ? x : MathF.Sign(x) * (0.8f + 0.2f * MathF.Tanh((MathF.Abs(x) - 0.8f) / 0.2f));
                peak = MathF.Max(peak, MathF.Abs(x));
            }
        }
        LastPeak = peak;
    }
}
