using System.Runtime.InteropServices;
using SDL3;

namespace TickHappens.Engine.Audio;

/// <summary>
/// Default SDL3 playback stream fed from the main thread: each frame <see cref="Pump"/> tops the queue up to a
/// short latency target with <see cref="AudioMixer"/> output, so the mixer never runs on the audio thread.
/// </summary>
public sealed class AudioDevice : IDisposable
{
    private readonly nint _stream;
    private readonly float[] _buffer = new float[AudioMixer.Rate / 5 * 2];
    private const float Latency = 0.07f;

    public AudioMixer Mixer { get; }

    private AudioDevice(nint stream, AudioMixer mixer)
    {
        _stream = stream;
        Mixer = mixer;
    }

    /// <summary>Opens the default device, or returns null (with a reason) when there is none.</summary>
    public static AudioDevice? TryOpen(AudioMixer mixer, out string? error)
    {
        error = null;
        if (!SDL.InitSubSystem(SDL.InitFlags.Audio))
        {
            error = SDL.GetError();
            return null;
        }
        var spec = new SDL.AudioSpec { Format = SDL.AudioFormat.AudioF32LE, Channels = 2, Freq = AudioMixer.Rate };
        nint stream = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in spec, null, 0);
        if (stream == 0)
        {
            error = SDL.GetError();
            return null;
        }
        SDL.ResumeAudioStreamDevice(stream);
        return new AudioDevice(stream, mixer);
    }

    /// <summary>Mixes enough audio to keep about max(70 ms, 1.5 frames) queued.</summary>
    public void Pump(float frameDt)
    {
        int queuedFrames = SDL.GetAudioStreamQueued(_stream) / 8;
        int target = (int)(MathF.Max(Latency, frameDt * 1.5f) * AudioMixer.Rate);
        int need = Math.Min(target - queuedFrames, _buffer.Length / 2);
        if (need <= 0) return;
        var span = _buffer.AsSpan(0, need * 2);
        Mixer.Mix(span);
        SDL.PutAudioStreamData(_stream, MemoryMarshal.AsBytes(span), need * 8);
    }

    public void Dispose() => SDL.DestroyAudioStream(_stream);
}
