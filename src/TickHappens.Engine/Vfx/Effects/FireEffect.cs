// Adapted from Sprite Fusion "fire". MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt.
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

/// <summary>Flame tongues along the top of the opaque sprite, with smoke and embers in world space.</summary>
public sealed class FireEffect(Func<ActorSilhouette?> silhouette, uint seed = 1, float duration = 2.6f) : IPixelEffect
{
    private static readonly uint[] Fire = [Hex("#fff3a6"), Hex("#ffc23d"), Hex("#ff8a2a"), Hex("#f2561d"), Hex("#8a1f17")];
    private static readonly uint Smoke = Hex("#5b5566");
    private readonly VfxRng _rng = new(seed);
    private readonly List<Particle> _particles = new(96);
    private readonly List<(int X, int Y)> _tips = new(32);
    private float _time, _stopAt = duration, _strength, _lean;
    private int? _lastX;

    private struct Particle
    {
        public float X, Y, Vx, Vy, Life, MaxLife;
        public bool Smoke;
    }

    public bool Done => _time > _stopAt + 1.2f && _particles.Count == 0;
    public void Stop() => _stopAt = MathF.Min(_stopAt, _time);

    public void Update(float dt)
    {
        _time += dt;
        _strength = Clamp(_time / 0.3f) * Clamp(1 - (_time - _stopAt) / 1.2f);
        _tips.Clear();
        if (_strength > 0 && silhouette() is { } sprite)
        {
            float motion = dt > 0 ? (sprite.X - (_lastX ?? sprite.X)) / dt : 0;
            _lean += (Clamp(-motion / 90, -1, 1) - _lean) * (1 - MathF.Exp(-7 * dt));
            _lastX = sprite.X;
            for (int x = 0; x < sprite.Width; x += 2)
                for (int y = 0; y < sprite.Height; y++)
                {
                    if (!sprite.IsOpaque(x, y, 128)) continue;
                    _tips.Add((sprite.X + x, sprite.Y + y));
                    if (_rng.Chance(0.13f * _strength))
                    {
                        bool smoke = _rng.Chance(0.34f);
                        float life = smoke ? _rng.Range(0.8f, 1.6f) : _rng.Range(0.2f, 0.65f);
                        _particles.Add(new Particle
                        {
                            X = sprite.X + x, Y = sprite.Y + y - 5, Vx = _rng.Range(-10, 10) + _lean * 20,
                            Vy = _rng.Range(-42, -20), Life = life, MaxLife = life, Smoke = smoke,
                        });
                    }
                    break;
                }
        }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0) { _particles.RemoveAt(i); continue; }
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            _particles[i] = p;
        }
    }

    public void Draw(PixelFrame frame, VfxLayer layer)
    {
        var color = PixelCanvas.ColorOf(frame);
        var light = PixelCanvas.LightOf(frame);
        foreach (var p in _particles)
        {
            float k = p.Life / p.MaxLife;
            if (p.Smoke && layer != VfxLayer.Front)
                color.AlphaDisc(p.X, p.Y, 2 + 4 * (1 - k), Smoke, 0.45f * k);
            if (!p.Smoke && layer != VfxLayer.Back)
                color.Dot(p.X, p.Y, Ramp(Fire, 1 - k), Clamp(k * 2));
        }
        if (layer == VfxLayer.Back) return;
        for (int i = 0; i < _tips.Count; i++)
        {
            var (x, y) = _tips[i];
            float h = (7 + 5 * Hash(i, 2) + 3 * Noise(_time * 9 + i, i)) * _strength;
            for (int row = 0; row < h; row++)
            {
                float k = row / h;
                float px = x + (Noise(_time * 6 + i, 4) * 2 + _lean * h) * k;
                color.Disc(px, y + 2 - row, 2.5f * (1 - k), Ramp(Fire, k));
            }
            if (i % 5 == 0) light.Glow(x, y - 4, 30, Fire[2], 0.34f * _strength);
        }
    }
}
