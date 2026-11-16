using TickHappens.AssetTool.Audio;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Audio;
using TickHappens.Engine.Vfx;
using TickHappens.Engine.Vfx.Effects;

namespace TickHappens.Tests;

public class AudioTests
{
    private static string Temp(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "tickhappens-audio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static AudioClip Tone(float seconds, float amplitude = 1)
    {
        var s = new float[(int)(seconds * AudioMixer.Rate)];
        for (int i = 0; i < s.Length; i++) s[i] = amplitude * MathF.Sin(i * 0.11f);
        return new AudioClip("tone", s, 1, AudioMixer.Rate);
    }

    [Fact]
    public void Wav_round_trips_16_bit_pcm()
    {
        string path = Temp("a.wav");
        float[] data = [0, 0.5f, -0.5f, 1, -1, 0.25f];
        WavFile.Write(path, data, 2, AudioMixer.Rate);
        var clip = WavFile.Read(path);
        Assert.Equal(2, clip.Channels);
        Assert.Equal(AudioMixer.Rate, clip.Rate);
        Assert.Equal(3, clip.Frames);
        for (int i = 0; i < data.Length; i++) Assert.Equal(data[i], clip.Samples[i], 3);
    }

    [Fact]
    public void Hundred_hits_per_frame_never_clip_and_respect_the_instance_cap()
    {
        var mixer = new AudioMixer { MasterVolume = 1, SfxVolume = 1 };
        var hit = Tone(0.2f);
        var block = new float[AudioMixer.Rate / 60 * 2];
        float peak = 0;
        for (int frame = 0; frame < 30; frame++)
        {
            for (int k = 0; k < 100; k++) mixer.Play(hit, 1, 1, (k % 3 - 1) * 0.5f, maxInstances: 6);
            mixer.Mix(block);
            peak = MathF.Max(peak, mixer.LastPeak);
            Assert.True(mixer.ActiveVoices <= 6);
            Assert.All(block, x => Assert.True(float.IsFinite(x)));
        }
        Assert.True(peak <= 1f, $"peak {peak}");
        Assert.True(peak > 0.5f, "the limiter should not silence the mix");
    }

    [Fact]
    public void Voice_pool_steals_the_oldest_voice_when_full()
    {
        var mixer = new AudioMixer();
        var clips = Enumerable.Range(0, AudioMixer.MaxVoices + 10).Select(_ => Tone(1)).ToArray();
        foreach (var c in clips) mixer.Play(c, 0.1f);
        Assert.Equal(AudioMixer.MaxVoices, mixer.ActiveVoices);
    }

    [Fact]
    public void Music_crossfades_and_loops()
    {
        var mixer = new AudioMixer { MusicVolume = 1 };
        var a = Tone(0.1f, 0.5f);
        var b = Tone(0.1f, 0.5f);
        mixer.PlayMusic(a, 1, 0.05f);
        var block = new float[AudioMixer.Rate / 2 * 2];  // 0.5 s: five passes of the 0.1 s loop
        mixer.Mix(block);
        Assert.Same(a, mixer.CurrentMusic);
        Assert.True(AudioBuilder.Rms(block.AsSpan(block.Length / 2)) > 0.2f, "the loop keeps playing past its end");
        mixer.PlayMusic(b, 1, 0.05f);
        mixer.Mix(block);
        Assert.Same(b, mixer.CurrentMusic);
    }

    [Fact]
    public void Exclusive_pause_music_replaces_a_crossfade_and_run_music_resumes_in_place()
    {
        var mixer = new AudioMixer { MusicVolume = 1 };
        var reference = new AudioMixer { MusicVolume = 1 };
        var menu = new AudioClip("menu", [-0.1f, -0.2f], 1, AudioMixer.Rate);
        var run = new AudioClip("run", [0.1f, 0.2f, 0.3f], 1, AudioMixer.Rate);
        var pause = new AudioClip("pause", [0.5f, 0.5f], 1, AudioMixer.Rate);
        var block = new float[2048];
        var referenceBlock = new float[2048];

        mixer.PlayMusic(menu, 1, 0.01f);
        mixer.Mix(block);
        mixer.PlayMusic(run, 1, 0.05f);
        reference.PlayMusic(run, 1, 0.05f);
        mixer.Mix(block);
        reference.Mix(referenceBlock);

        mixer.SetExclusiveMusic(pause);
        mixer.Mix(block);
        Assert.Same(pause, mixer.CurrentMusic);
        Assert.All(block[1600..], sample => Assert.Equal(0.5f, sample));

        mixer.SetExclusiveMusic(null);
        mixer.Mix(block);
        reference.Mix(referenceBlock);
        Assert.Same(run, mixer.CurrentMusic);
        Assert.Equal(referenceBlock, block);
    }

    [Fact]
    public void Sound_and_music_recipes_render_deterministically()
    {
        string dir = Path.GetDirectoryName(Temp("x"))!;
        string sfx = Path.Combine(dir, "zap.sound.json"), music = Path.Combine(dir, "loop.music.json");
        File.WriteAllText(sfx, """
            { "name": "zap", "variants": 3, "seed": 5, "vary": 0.1, "echo": { "delay": 40, "feedback": 0.3, "mix": 0.2 },
              "layers": [ { "type": "tone", "freq": [900, 300], "decay": 40, "wave": "square" }, { "type": "noise", "highpass": 2000, "decay": 10 } ] }
            """);
        File.WriteAllText(music, """
            { "name": "loop", "seed": 3, "bpm": 160, "root": 62,
              "sections": { "A": { "bars": 4, "chords": [0, 5, 2, 6] } }, "form": ["A"],
              "tracks": [
                { "role": "lead", "wave": "pulse", "duty": 0.25, "echo": 0.4 },
                { "role": "bass", "wave": "triangle", "octave": 2, "pattern": "x-x-o-x-" },
                { "role": "drum", "pattern": "x...x...", "layers": [ { "type": "tone", "freq": [150, 45], "decay": 60 } ] } ],
              "echo": { "beats": 0.75 }, "reverb": { "mix": 0.3 } }
            """);
        var s1 = SoundSynth.Generate(sfx);
        var s2 = SoundSynth.Generate(sfx);
        Assert.Equal(3, s1.Variants.Count);
        for (int v = 0; v < 3; v++) Assert.Equal(s1.Variants[v], s2.Variants[v]);
        Assert.False(s1.Variants[0].SequenceEqual(s1.Variants[1]), "variants are jittered");
        Assert.All(s1.Variants, v => Assert.True(v.Max(MathF.Abs) <= 0.8001f));

        var m1 = MusicComposer.Compose(music);
        var m2 = MusicComposer.Compose(music);
        Assert.Equal(m1.Stereo, m2.Stereo);
        // Four bars of 16th notes at 160 BPM, stereo.
        Assert.Equal((int)Math.Round(4 * 16 * (60.0 / 160 / 4) * AudioMixer.Rate) * 2, m1.Stereo.Length);
        Assert.True(AudioBuilder.Rms(m1.Stereo) > 0.05f);
        Assert.True(m1.Stereo.Max(MathF.Abs) <= 0.86f);
    }

    [Fact]
    public void Recipe_errors_name_the_file_and_key()
    {
        string path = Temp("bad.sound.json");
        File.WriteAllText(path, """{ "name": "bad", "layers": [ { "type": "tone", "wobble": 1 } ] }""");
        var e = Assert.Throws<InvalidDataException>(() => SoundSynth.Generate(path));
        Assert.Contains("wobble", e.Message);
        Assert.Contains("bad.sound.json", e.Message);
    }

    [Fact]
    public void Assets_audio_covers_every_recipe()
    {
        string root = AssetPaths.SourceRoot(FindGameRoot());
        string src = Path.Combine(root, "art", "audio");
        if (!Directory.Exists(src)) return;  // an Assets checkout from before audio
        foreach (var f in Directory.EnumerateFiles(Path.Combine(src, "sfx"), "*.sound.json"))
        {
            string name = Path.GetFileName(f).Split('.')[0];
            Assert.True(File.Exists(Path.Combine(root, "assets", "audio", "sfx", name + ".1.wav")), $"{name} is not built; run AssetTool audio");
        }
        foreach (var f in Directory.EnumerateFiles(Path.Combine(src, "music"), "*.music.json"))
        {
            string name = Path.GetFileName(f).Split('.')[0];
            var clip = WavFile.Read(Path.Combine(root, "assets", "audio", "music", name + ".wav"));
            Assert.Equal(2, clip.Channels);
            Assert.Equal(AudioMixer.Rate, clip.Rate);
        }
    }

    [Fact]
    public void Dash_splash_is_seeded_and_finishes()
    {
        static uint[] Render(uint seed)
        {
            var fx = new DashSplashEffect(new DashSplashOptions { X = 100, Y = 100, Dx = 1, Dy = 0, Seed = seed });
            var frame = new PixelFrame(256, 256);
            for (int i = 0; i < 12; i++) fx.Update(1 / 60f);
            fx.Draw(frame, VfxLayer.All);
            return frame.Color;
        }
        Assert.Equal(Render(4), Render(4));
        Assert.NotEqual(Render(4), Render(5));
        Assert.Contains(Render(4), c => c != 0);
        var done = new DashSplashEffect(new DashSplashOptions { Seed = 1 });
        for (int i = 0; i < 120 && !done.Done; i++) done.Update(1 / 60f);
        Assert.True(done.Done);
    }

    private static string FindGameRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "TickHappens.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("TickHappens.sln");
    }
}
