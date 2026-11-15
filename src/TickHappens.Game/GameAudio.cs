using System.Numerics;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Audio;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

/// <summary>
/// Local sound for a run: turns simulation events into effects panned and attenuated around the listener,
/// and switches between the menu and run music. Purely cosmetic and never networked; with no audio device or
/// no Assets audio folder the game stays silent. <c>--record-audio</c> mixes exactly one frame of audio per
/// rendered frame into a WAV instead of a device, so automated runs can be measured.
/// </summary>
internal sealed class GameAudio : IDisposable
{
    private const int MaxHitsPerFrame = 3, MaxKillsPerFrame = 3, MaxPickupsPerFrame = 2;

    private readonly AudioMixer _mixer = new();
    private readonly AudioDevice? _device;
    private readonly Dictionary<string, AudioClip[]> _sfx = [];
    private readonly AudioClip? _menuMusic, _runMusic;
    private readonly string? _recordPath;
    private readonly List<float> _recorded = [];
    private readonly float[] _recordBlock = new float[AudioMixer.Rate / 5 * 2];
    private double _recordDebt;
    private uint _rng = 0x9E3779B9u;
    private float _xpCombo;
    private bool _wasDead, _paused;

    public bool Enabled { get; }

    public GameAudio(bool openDevice, string? recordPath)
    {
        _recordPath = recordPath;
        string dir;
        try { dir = AssetPaths.Get("audio"); }
        catch (DirectoryNotFoundException) { return; }
        string sfx = Path.Combine(dir, "sfx"), music = Path.Combine(dir, "music");
        if (!Directory.Exists(sfx))
        {
            Console.Error.WriteLine($"No audio in {dir}; run AssetTool audio in Assets. Playing silently.");
            return;
        }
        foreach (var group in Directory.EnumerateFiles(sfx, "*.wav").Order().GroupBy(f => Path.GetFileName(f).Split('.')[0]))
            _sfx[group.Key] = [.. group.Select(WavFile.Read)];
        string menu = Path.Combine(music, "menu_theme.wav"), run = Path.Combine(music, "run_theme.wav");
        if (File.Exists(menu)) _menuMusic = WavFile.Read(menu);
        if (File.Exists(run)) _runMusic = WavFile.Read(run);
        if (openDevice && recordPath == null)
        {
            _device = AudioDevice.TryOpen(_mixer, out string? error);
            if (_device == null) Console.Error.WriteLine($"Audio device unavailable ({error}); playing silently.");
        }
        Enabled = _device != null || recordPath != null;
    }

    private float Rand() => ((_rng = _rng * 1664525u + 1013904223u) >> 8) / 16777216f;

    public void Play(string name, float volume = 1, float pan = 0, float pitchJitter = 0.06f, int cap = 4, float pitch = 1)
    {
        if (!Enabled || !_sfx.TryGetValue(name, out var clips)) return;
        var clip = clips[(int)(Rand() * clips.Length) % clips.Length];
        _mixer.Play(clip, volume, pitch * (1 + (Rand() * 2 - 1) * pitchJitter), pan, cap);
    }

    public void MenuMusic()
    {
        if (Enabled && _menuMusic != null) _mixer.PlayMusic(_menuMusic, 0.9f, 1.2f);
        _wasDead = false;
    }

    public void RunMusic()
    {
        if (Enabled && _runMusic != null) _mixer.PlayMusic(_runMusic, 1f, 0.8f);
        _wasDead = false;
    }

    /// <summary>Plays this frame's events; <paramref name="listener"/> is the local hero's position.</summary>
    public void Consume(RunWorld world, Vector2 listener, bool paused)
    {
        if (!Enabled) return;
        if (paused != _paused)
        {
            _paused = paused;
            if (world.Phase == RunPhase.Playing) _mixer.SetMusicLevel(paused ? 0.35f : 1f, 0.3f);
        }
        bool dead = world.Phase == RunPhase.Dead;
        if (dead && !_wasDead)
        {
            Play("death", 0.9f, 0, 0, 1);
            _mixer.SetMusicLevel(0.2f, 2f);
        }
        _wasDead = dead;
        int hits = 0, kills = 0, pickups = 0;
        foreach (var e in world.Events)
        {
            float dx = e.X - listener.X, dy = e.Y - listener.Y, d = MathF.Sqrt(dx * dx + dy * dy);
            float near = Math.Clamp(1 - (d - 120) / 320, 0.3f, 1f), pan = Math.Clamp(dx / 240, -0.8f, 0.8f);
            switch (e.Kind)
            {
                case EventKind.Hit when hits++ < MaxHitsPerFrame:
                    Play("hit", 0.3f * near, pan, 0.12f, 4, e.A == 1 ? 1.25f : 1f);
                    break;
                case EventKind.Kill when kills++ < MaxKillsPerFrame:
                    Play("kill", 0.42f * near, pan, 0.1f, 4);
                    break;
                case EventKind.Bolt:
                    Play("bolt", 0.2f * near, pan, 0.08f, 3);
                    break;
                case EventKind.Pulse:
                    Play("pulse", 0.45f * near, pan, 0.05f, 2);
                    break;
                case EventKind.Chain:
                    Play("chain", 0.35f * near, pan, 0.1f, 2);
                    break;
                case EventKind.Blast:
                    Play("blast", 0.65f * near, pan, 0.08f, 3);
                    break;
                case EventKind.LevelUp:
                    Play("levelup", 0.55f, pan, 0, 1);
                    break;
                case EventKind.Statue:
                    Play("statue", 0.75f, pan, 0, 1);
                    break;
                case EventKind.Chest:
                    Play("chest", 0.65f, pan, 0, 2);
                    break;
                case EventKind.PlayerHurt:
                    Play("hurt", 0.6f * near, pan, 0.08f, 2);
                    break;
                case EventKind.Dash:
                    Play("dash", 0.65f * near, pan, 0.06f, 2);
                    break;
                case EventKind.Elite:
                    Play("elite", 0.6f, pan, 0, 1);
                    break;
                case EventKind.Pickup when pickups++ < MaxPickupsPerFrame:
                    if (e.A == (int)PickupKind.Gold) Play("gold", 0.35f * near, pan, 0.03f, 3);
                    else
                    {
                        // Collecting a stream of crystals climbs in pitch, then settles.
                        Play("xp", 0.22f * near, pan, 0.02f, 3, 1 + MathF.Min(_xpCombo, 12) * 0.04f);
                        _xpCombo += 1;
                    }
                    break;
            }
        }
    }

    /// <summary>Feeds the device (or the recording) for this frame.</summary>
    public void Update(float frameDt)
    {
        _xpCombo = MathF.Max(0, _xpCombo - frameDt * 6);
        if (_device != null) _device.Pump(frameDt);
        else if (_recordPath != null)
        {
            _recordDebt += frameDt * AudioMixer.Rate;
            int frames = Math.Min((int)_recordDebt, _recordBlock.Length / 2);
            _recordDebt -= frames;
            var block = _recordBlock.AsSpan(0, frames * 2);
            _mixer.Mix(block);
            foreach (float x in block) _recorded.Add(x);
        }
    }

    public void Dispose()
    {
        _device?.Dispose();
        if (_recordPath != null)
        {
            WavFile.Write(_recordPath, _recorded.ToArray(), 2, AudioMixer.Rate);
            Console.WriteLine($"audio -> {_recordPath} ({_recorded.Count / 2 / (float)AudioMixer.Rate:F1} s, mixer peak {_mixer.LastPeak:F2})");
        }
    }
}
