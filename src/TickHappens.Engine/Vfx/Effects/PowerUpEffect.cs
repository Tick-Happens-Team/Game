// Adapted from Sprite Fusion "power-up". MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt.
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

/// <summary>Gather, burst, and sustain around the actor's current sprite.</summary>
public sealed class PowerUpEffect(Func<ActorSilhouette?> silhouette, uint seed = 1) : IPixelEffect
{
    private const float Burst = 0.32f;
    private static readonly uint Ice = Hex("#8ff0ff"), Blue = Hex("#3fd0ff"), White = Hex("#e0fcff");
    private readonly VfxRng _rng = new(seed);
    private readonly List<Mote> _motes = new(96);
    private ActorSilhouette? _sprite;
    private float _time, _x, _feet;

    private struct Mote
    {
        public float X, Y, Vx, Vy, Life, MaxLife;
    }

    public bool Done => _time >= 2.95f;

    public void Update(float dt)
    {
        float previous = _time;
        _time += dt;
        _sprite = silhouette();
        if (_sprite is { } sprite)
        {
            _x = sprite.X + sprite.Width / 2f;
            _feet = sprite.Y + sprite.Height;
            if (_time < Burst)
            {
                for (int i = 0; i < 3; i++) Spawn(true);
            }
            else if (previous < Burst)
            {
                for (int i = 0; i < 22; i++) Spawn(false);
            }
            else if (_time < 1.75f && _rng.Chance(0.34f))
            {
                Spawn(false);
            }
        }

        for (int i = _motes.Count - 1; i >= 0; i--)
        {
            var mote = _motes[i];
            mote.Life -= dt;
            if (mote.Life <= 0) { _motes.RemoveAt(i); continue; }
            mote.X += mote.Vx * dt;
            mote.Y += mote.Vy * dt;
            _motes[i] = mote;
        }
    }

    private void Spawn(bool inward)
    {
        float angle = _rng.Range(0, Tau), radius = inward ? _rng.Range(24, 42) : _rng.Range(3, 9);
        float vx = MathF.Cos(angle), vy = MathF.Sin(angle) * 0.8f;
        float speed = inward ? -175 : _rng.Range(45, 100);
        float life = inward ? radius / -speed : _rng.Range(0.25f, 0.85f);
        _motes.Add(new Mote
        {
            X = _x + vx * radius, Y = _feet - 12 + vy * radius,
            Vx = vx * speed, Vy = vy * speed - (inward ? 0 : 22), Life = life, MaxLife = life,
        });
    }

    public void Draw(PixelFrame frame, VfxLayer layer)
    {
        var color = PixelCanvas.ColorOf(frame);
        var light = PixelCanvas.LightOf(frame);
        if (_sprite is { } sprite)
        {
            float aura = Clamp((_time - Burst + 0.04f) / 0.16f) *
                Clamp((2.35f - _time) / 0.6f) * (0.85f + 0.15f * MathF.Sin(_time * 13));
            if (layer != VfxLayer.Front && aura > 0)
            {
                light.Glow(_x, _feet - 10, 48, Blue, 0.45f * aura);
                float elapsed = _time - Burst;
                if (elapsed is >= 0 and < 0.35f)
                    RingE(color, _x, _feet - 3, 8 + elapsed * 140, 3 + elapsed * 55,
                        2, Ice, 1 - elapsed / 0.35f, static _ => true);
            }
            if (layer != VfxLayer.Back && aura > 0)
                for (int y = -1; y <= sprite.Height; y++)
                    for (int x = -1; x <= sprite.Width; x++)
                        if (!sprite.IsOpaque(x, y) && (sprite.IsOpaque(x + 1, y) || sprite.IsOpaque(x - 1, y) ||
                            sprite.IsOpaque(x, y + 1) || sprite.IsOpaque(x, y - 1)))
                            color.Dot(sprite.X + x, sprite.Y + y, Ice, aura);
        }

        if (layer == VfxLayer.Back) return;
        foreach (var mote in _motes)
        {
            float k = mote.Life / mote.MaxLife;
            color.Dot(mote.X, mote.Y, White, Clamp(k * 2));
            light.Glow(mote.X, mote.Y, 4, Blue, 0.3f * k);
        }
    }
}
