// Adapted from Sprite Fusion "embers". MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt.
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

/// <summary>Rising sparks, cinders, and ash. Positions use two simulation units per art pixel.</summary>
public sealed class EmbersEffect(float x, float bottom, float width, uint seed = 1) : IPixelEffect
{
    private static readonly uint Hot = Hex("#fff3a6"), Orange = Hex("#ff8a2a"), Red = Hex("#c9281f");
    private static readonly uint Ash = Hex("#5a5160"), Heat = Hex("#ff7832");
    private readonly VfxRng _rng = new(seed);
    private readonly List<Ember> _embers = new(256);
    private float _time, _spawnTime;
    private bool _stopped;

    private struct Ember
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Phase;
        public bool Cinder, Ash;
    }

    public bool Done => _stopped && _embers.Count == 0;
    public void Stop() => _stopped = true;

    private void Spawn(bool cinder, bool ash)
    {
        float life = ash ? _rng.Range(5, 7) : cinder ? _rng.Range(3.5f, 6) : _rng.Range(2.2f, 4.2f);
        _embers.Add(new Ember
        {
            X = x + _rng.Range(0, width), Y = bottom + _rng.Range(-10, 15),
            Vx = _rng.Range(-40, 40), Vy = cinder ? _rng.Range(-110, -30) : _rng.Range(-240, -90),
            Life = life, MaxLife = life, Phase = _rng.Range(0, 100), Cinder = cinder, Ash = ash,
        });
    }

    public void Update(float dt)
    {
        _time += dt;
        if (!_stopped)
        {
            _spawnTime += dt * 60;
            while (_spawnTime >= 1)
            {
                _spawnTime--;
                for (int i = 0; i < 2; i++) if (_rng.Chance(0.8f)) Spawn(false, false);
                if (_rng.Chance(0.22f)) Spawn(true, false);
                if (_rng.Chance(0.05f)) Spawn(true, true);
            }
        }
        for (int i = _embers.Count - 1; i >= 0; i--)
        {
            var p = _embers[i];
            p.Life -= dt;
            if (p.Life <= 0) { _embers.RemoveAt(i); continue; }
            p.Vx += (90 * MathF.Cos(_time * 2.3f + p.Phase) - 1.6f * p.Vx) * dt;
            if (!p.Cinder) p.Vy += (-60 + 40 * MathF.Sin(_time * 3 + p.Phase) - 1.6f * p.Vy) * dt;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            _embers[i] = p;
        }
    }

    public void Draw(PixelFrame frame, VfxLayer layer)
    {
        var color = PixelCanvas.ColorOf(frame);
        var light = PixelCanvas.LightOf(frame);
        if (layer != VfxLayer.Front)
        {
            float fade = _stopped ? Clamp(_embers.Count / 60f) : 1;
            int start = (int)MathF.Floor(x / 2);
            int end = (int)MathF.Ceiling((x + width) / 2);
            for (int col = start; col < end; col++)
            {
                float k = (0.7f + 0.3f * Noise(col * 0.02f + _time * 0.6f, 7)) * fade;
                for (int row = 0; row < 22; row++)
                    color.Dot(col, bottom / 2 - row, Red, 0.4f * k * (1 - row / 22f));
                if (col % 24 == 0) light.Glow(col, bottom / 2 + 6, 60, Heat, 0.2f * k);
            }
        }
        if (layer == VfxLayer.Back) return;
        foreach (var p in _embers)
        {
            float k = p.Life / p.MaxLife, px = p.X / 2, py = p.Y / 2;
            uint c = p.Ash ? Ash : k > 0.5f && MathF.Sin(_time * 22 + p.Phase) > -0.2f ? Hot : k > 0.2f ? Orange : Red;
            color.Dot(px, py, c, Clamp(k * 3));
            if (p.Cinder) color.Dot(px, py + 1, p.Ash ? Ash : Orange, Clamp(k * 2));
            if (!p.Ash) light.Glow(px, py, p.Cinder ? 6 : 3, c, 0.3f * k);
        }
    }
}
