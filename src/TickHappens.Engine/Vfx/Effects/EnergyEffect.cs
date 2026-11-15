// Adapted from Sprite Fusion "energy". MIT License, Copyright (c) 2026 Hugo Duprez.
// See ../LICENSE-SpriteFusion-VFX.txt.
using static TickHappens.Engine.Vfx.VfxMath;

namespace TickHappens.Engine.Vfx.Effects;

public enum EnergyKind { Charge, Beam, Orb, Projectile }

/// <summary>Seeded energy cast. Coordinates use two simulation units per art pixel.</summary>
public sealed class EnergyEffect(EnergyKind kind, float x, float y, float angle = 0, uint seed = 1) : IPixelEffect
{
    private const float ChargeTime = 0.46f, End = 1.35f;
    private static readonly uint Core = Hex("#e9fdff"), Body = Hex("#50dbff"), Rim = Hex("#4b51e2");
    private readonly VfxRng _rng = new(seed);
    private readonly List<Spark> _sparks = new(96);
    private readonly float _dx = MathF.Cos(angle), _dy = MathF.Sin(angle);
    private float _t, _spawnTime;

    private struct Spark
    {
        public float Angle, Radius, Speed, Life, MaxLife;
    }

    public bool Done => _t >= End;

    public void Update(float dt)
    {
        _t += dt;
        _spawnTime += dt * 60;
        while (_spawnTime >= 1)
        {
            _spawnTime--;
            if (_t >= End - 0.35f) continue;
            bool gathering = _t < ChargeTime;
            float life = _rng.Range(0.17f, 0.35f);
            _sparks.Add(new Spark
            {
                Angle = _rng.Range(0, Tau), Radius = gathering ? _rng.Range(18, 30) : _rng.Range(2, 8),
                Speed = gathering ? _rng.Range(-95, -55) : _rng.Range(32, 65),
                Life = life, MaxLife = life,
            });
        }

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var spark = _sparks[i];
            spark.Life -= dt;
            spark.Radius += spark.Speed * dt;
            if (spark.Life <= 0 || spark.Radius <= 0) { _sparks.RemoveAt(i); continue; }
            _sparks[i] = spark;
        }
    }

    public void Draw(PixelFrame frame, VfxLayer layer)
    {
        if (layer == VfxLayer.Back || Done) return;
        var color = PixelCanvas.ColorOf(frame);
        var light = PixelCanvas.LightOf(frame);
        float release = MathF.Max(0, _t - ChargeTime), fade = Clamp((End - _t) / 0.32f);
        float distance = kind == EnergyKind.Projectile ? release * 155 : 0;
        float cx = x / 2 + _dx * distance, cy = y / 2 + _dy * distance;
        float radius = (2 + 5 * Clamp(_t / ChargeTime)) * fade;
        if (kind == EnergyKind.Charge) radius *= (1 + 0.14f * MathF.Sin(_t * 38));
        if (kind == EnergyKind.Orb && release > 0) radius *= 1.6f;

        if (kind == EnergyKind.Beam && release > 0)
        {
            float length = 92 * OutCubic(release / 0.17f);
            float width = (4 + MathF.Sin(_t * 38)) * fade;
            for (float at = 0; at < length; at += 2)
            {
                float bx = cx + at * _dx, by = cy + at * _dy;
                color.Disc(bx, by, width, Rim);
                color.Disc(bx, by, 0.7f * width, Body);
                color.Disc(bx, by, 0.3f * width, Core);
                if ((int)at % 10 == 0) light.Glow(bx, by, 19, Body, 0.35f * fade);
            }
        }

        if (radius > 0.4f)
        {
            color.Disc(cx, cy, radius, Rim);
            color.Disc(cx, cy - 1, radius * 0.78f, Body);
            color.Disc(cx - 1, cy - 2, radius * 0.47f, Core);
            light.Glow(cx, cy, 24 + radius, Body, 0.5f * fade);
        }

        foreach (var spark in _sparks)
        {
            float sx = cx + MathF.Cos(spark.Angle) * spark.Radius;
            float sy = cy + MathF.Sin(spark.Angle) * spark.Radius;
            float k = spark.Life / spark.MaxLife;
            color.Dot(sx, sy, Core, Clamp(k * 2));
            light.Glow(sx, sy, 5, Body, 0.25f * k);
        }
    }
}
