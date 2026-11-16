using System.Numerics;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

internal sealed partial class RunView
{
    private const int MaxItemEffects = 40;
    private const uint RevivalMint = 0xFFAEF5B0u, RevivalGold = 0xFF9ADFFFu;
    private readonly List<ItemEffectFrame> _itemEffects = new(MaxItemEffects);
    private readonly record struct ItemEffectFrame(GameEvent Event, float Age, float Duration);
    private readonly int _hookahItem;
    private readonly SiphonBeam[] _siphonBeams;
    private readonly record struct SiphonBeam(int PlayerId, int EnemyUid, float Until);
    private readonly SkateMark[] _skateTrail = new SkateMark[4 * RunWorld.TrailCapacity];
    private readonly SkateMark[] _lastSkateMarks = new SkateMark[4];
    private int _nextSkateMark;
    private readonly record struct SkateMark(Vector2 Start, Vector2 End, float Until);

    private void AddSkateMark(GameEvent e)
    {
        if ((uint)e.A >= (uint)_lastSkateMarks.Length) return;
        var end = new Vector2(e.X, e.Y);
        var last = _lastSkateMarks[e.A];
        var start = last.Until > _time + RunWorld.HeroTrailSeconds - 0.3f && Vector2.DistanceSquared(last.End, end) <= 24 * 24
            ? last.End : end;
        var mark = new SkateMark(start, end, _time + RunWorld.HeroTrailSeconds);
        _lastSkateMarks[e.A] = mark;
        _skateTrail[_nextSkateMark] = mark;
        _nextSkateMark = (_nextSkateMark + 1) % _skateTrail.Length;
    }

    private void DrawSkateTrail(PixelCanvas color, PixelCanvas light)
    {
        for (int i = 0; i < _skateTrail.Length; i++)
        {
            var mark = _skateTrail[(_nextSkateMark + i) % _skateTrail.Length];
            float fade = Math.Clamp(mark.Until - _time, 0, 1);
            if (fade <= 0) continue;
            int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(mark.Start, mark.End) / 5));
            for (int step = 1; step <= steps; step++)
            {
                var point = Vector2.Lerp(mark.Start, mark.End, step / (float)steps);
                color.AlphaDisc(point.X, point.Y, 5, 0xFF1B4EB8u, fade * 0.65f);
                color.AlphaDisc(point.X, point.Y, 2.8f, 0xFF33B4F4u, fade * 0.8f);
            }
            light.Glow(mark.End.X, mark.End.Y, 8, 0xFF2B9CFFu, fade * 0.45f);
        }
    }

    private void DrawHookahMist(RunWorld world, PixelCanvas color, float alpha)
    {
        if (_hookahItem < 0) return;
        foreach (var player in world.Players)
        {
            if (!player.Active || player.Hp <= 0 || player.ItemStacks[_hookahItem] == 0 ||
                player.StolenItem == _hookahItem) continue;
            var center = Vector2.Lerp(player.Prev, player.Pos, alpha);
            for (int i = 0; i < 8; i++)
            {
                float life = (_time * 0.8f + i / 8f) % 1;
                float angle = i * 2.4f + _time * 0.4f;
                var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (6 + 20 * life);
                color.AlphaDisc(center.X + offset.X, center.Y + offset.Y - 4, 3 + 5 * life,
                    0xFFB5B0CCu, 0.20f * MathF.Sin(life * MathF.PI));
            }
        }
    }

    private void DrawSiphonBeams(RunWorld world, PixelCanvas color, PixelCanvas light, float alpha)
    {
        for (int slot = 0; slot < world.EnemyHighWater; slot++)
        {
            var beam = _siphonBeams[slot];
            if (beam.Until <= _time || !world.EAlive[slot] || (world.EUid[slot] & ushort.MaxValue) != beam.EnemyUid)
                continue;
            var player = world.Players[beam.PlayerId];
            if (!player.Active || player.Hp <= 0) continue;

            var from = _displayPlayers[beam.PlayerId] - new Vector2(0, 9);
            var to = world.EnemyPos(slot, alpha) - new Vector2(0, 7);
            var delta = to - from;
            float length = delta.Length();
            if (length < 2 || length > 155) continue;

            var normal = new Vector2(-delta.Y, delta.X) / length;
            color.Line(from.X - normal.X, from.Y - normal.Y, to.X - normal.X, to.Y - normal.Y, 0xFF43A326u);
            color.Line(from.X + normal.X, from.Y + normal.Y, to.X + normal.X, to.Y + normal.Y, 0xFF43A326u);
            color.Line(from.X, from.Y, to.X, to.Y, 0xFF91F451u);
            light.Line(from.X, from.Y, to.X, to.Y, 0xFF58B934u);
            for (int i = 0; i < 3; i++)
            {
                float progress = (_time * 1.9f + i / 3f) % 1;
                var pulse = from + delta * progress;
                color.Disc(pulse.X, pulse.Y, 1.4f, 0xFFCDFB98u);
            }
        }
    }

    private void AddItemEffect(GameEvent e)
    {
        if (_itemEffects.Count == MaxItemEffects)
        {
            if (e.Kind != EventKind.Revival) return;
            _itemEffects.RemoveAt(0);
        }
        float duration = e.Kind switch
        {
            EventKind.Revival => 0.95f,
            EventKind.EnemyQuake => 0.52f,
            EventKind.WildAura => 0.65f,
            EventKind.Laser or EventKind.Rifle or EventKind.RocketImpact => 0.28f,
            _ => 0.5f,
        };
        _itemEffects.Add(new ItemEffectFrame(e, 0, duration));
        if (e.Kind == EventKind.Revival) AddFlash(e.X, e.Y - 12, 90, 0.65f, 1.3f, 0.85f, 0.65f);
    }

    private void DrawItemEffects(RunWorld world, PixelCanvas color, PixelCanvas light, float dt,
        int ox, int oy, int width, int height)
    {
        foreach (var player in world.Players)
        {
            var p = player.Pos;
            if (!player.Active || p.X < ox - 110 || p.X > ox + width + 110 ||
                p.Y < oy - 110 || p.Y > oy + height + 110) continue;
            if (player.Hp <= 0 && world.Players.Count > 1)
                DrawRevivalSite(color, light, p, player.RevivalTicks / (float)RunWorld.RevivalHoldTicks);
            else if (player.Hp > 0 && player.AcceleratorTicks > 0)
            {
                for (int i = -1; i <= 1; i++)
                {
                    var start = p - player.Facing * 7 + new Vector2(-player.Facing.Y, player.Facing.X) * (i * 5);
                    var end = start - player.Facing * (10 + 4 * MathF.Sin(_time * 32 + i * 2));
                    color.Line(start.X, start.Y - 8, end.X, end.Y - 8, i == 0 ? 0xFFC5FFFFu : 0xFF588DEFu);
                }
                light.Glow(p.X, p.Y - 8, 14, 0xFF60CAFFu, 0.65f);
            }
        }
        for (int i = _itemEffects.Count - 1; i >= 0; i--)
        {
            var frame = _itemEffects[i];
            float age = frame.Age + dt;
            if (age >= frame.Duration)
            {
                _itemEffects.RemoveAt(i);
                continue;
            }
            _itemEffects[i] = frame with { Age = age };
            DrawItemEffect(color, light, frame.Event, age / frame.Duration);
        }
    }

    private void DrawRevivalSite(PixelCanvas color, PixelCanvas light, Vector2 p, float progress)
    {
        color.Disc(p.X, p.Y, 9, 0xFF2D2938u);
        color.Disc(p.X, p.Y - 7, 6, 0xFF655E73u);
        color.Line(p.X, p.Y - 11, p.X, p.Y - 4, RevivalMint);
        color.Line(p.X - 3, p.Y - 8, p.X + 3, p.Y - 8, RevivalMint);
        const int rimDots = 80;
        for (int i = 0; i < rimDots; i++)
        {
            float angle = i * (MathF.Tau / rimDots) - MathF.PI / 2;
            bool charged = i < progress * rimDots;
            color.AlphaDisc(p.X + MathF.Cos(angle) * RunWorld.RevivalRadius,
                p.Y + MathF.Sin(angle) * RunWorld.RevivalRadius, charged ? 2 : 1.3f,
                charged ? RevivalMint : 0xFF6C9176u, charged ? 0.9f : 0.55f);
        }
        if (progress <= 0) return;
        light.Glow(p.X, p.Y - 9, 17 + progress * 14, RevivalMint, 0.5f + progress);
        for (int i = 0; i < 9; i++)
        {
            float angle = i * (MathF.Tau / 9) + _time * 3;
            float height = (_time * 35 + i * 7) % 32;
            float radius = 25 - 14 * progress;
            color.AlphaDisc(p.X + MathF.Cos(angle) * radius,
                p.Y + MathF.Sin(angle) * radius * 0.5f - height, 1.4f,
                i % 2 == 0 ? RevivalMint : RevivalGold, 1 - height / 38);
        }
    }

    private static void DrawItemEffect(PixelCanvas color, PixelCanvas light, GameEvent e, float t)
    {
        float x = e.X, y = e.Y, fade = 1 - t;
        float spread = 1 - fade * fade;
        float radius = Math.Clamp(e.Value, 1, 900);
        var rng = new SimRng(e.Seed);
        switch (e.Kind)
        {
            case EventKind.EagleDive:
            {
                float heading = MathF.Atan2(y - e.Y2, x - e.X2);
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        float angle = heading + side * (0.55f + i * 0.19f);
                        float distance = radius * spread * (0.4f + i * 0.06f);
                        float fx = x + MathF.Cos(angle) * distance, fy = y + MathF.Sin(angle) * distance;
                        color.Line(fx, fy, fx - MathF.Cos(angle - side * 0.5f) * fade * 15,
                            fy - MathF.Sin(angle - side * 0.5f) * fade * 15,
                            i % 2 == 0 ? 0xFFF0F9FFu : 0xFF73C0ECu);
                    }
                }
                light.Glow(x, y, 36 * fade, 0xFF85DBFFu, fade);
                break;
            }
            case EventKind.BoarStomp:
            case EventKind.WoodShockwave:
            {
                bool stomp = e.Kind == EventKind.BoarStomp;
                int cracks = stomp ? 20 : 36;
                uint main = stomp ? 0xFF6798C9u : 0xFF4FBB93u;
                uint tip = stomp ? 0xFFA1D4E9u : 0xFF83DBCAu;
                for (int i = 0; i < cracks; i++)
                {
                    float a = i * (MathF.Tau / cracks) + rng.Range(-0.06f, 0.06f);
                    float d = spread * radius * rng.Range(0.65f, 1);
                    float fx = x + MathF.Cos(a) * d, fy = y + MathF.Sin(a) * d;
                    float bend = a + (i % 2 == 0 ? 0.8f : -0.8f);
                    color.Line(fx, fy, fx - MathF.Cos(bend) * 26 * fade, fy - MathF.Sin(bend) * 26 * fade, main);
                    color.AlphaDisc(fx, fy, (stomp ? 4 : 2) * fade, tip, fade);
                }
                break;
            }
            case EventKind.EnemyQuake:
            {
                const uint Crust = 0xFFAD8967u, Fissure = 0xFF231B25u, Dust = 0xFFD1AE7Du;
                const int cracks = 8;
                for (int i = 0; i < cracks; i++)
                {
                    float heading = i * (MathF.Tau / cracks) + rng.Range(-0.22f, 0.22f);
                    var direction = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                    var across = new Vector2(-direction.Y, direction.X);
                    float length = radius * spread * rng.Range(0.72f, 1);
                    var start = new Vector2(x, y) + direction * 2;
                    for (int j = 1; j <= 3; j++)
                    {
                        var end = new Vector2(x, y) + direction * (length * j / 3) +
                            across * (spread * rng.Range(-5, 5));
                        if (t < 0.76f)
                        {
                            color.Line(start.X, start.Y + 1, end.X, end.Y + 1, Crust);
                            color.Line(start.X, start.Y, end.X, end.Y, Fissure);
                        }
                        start = end;
                    }
                    color.AlphaDisc(start.X, start.Y - t * 7, fade * 3.2f, Dust, fade * 0.64f);
                }
                break;
            }
            case EventKind.WildAura:
                for (int i = 0; i < 34; i++)
                {
                    float a = i * 2.399963f + t * 0.5f;
                    float d = radius * MathF.Sqrt((i + 0.5f) / 34) * (0.66f + 0.34f * spread);
                    float fx = x + MathF.Cos(a) * d, fy = y + MathF.Sin(a) * d;
                    color.AlphaDisc(fx, fy, 2.4f * fade, 0xFF70BD56u, fade);
                    color.Dot(fx + 2, fy - 2, 0xFF8EE4C1u, fade);
                }
                break;
            case EventKind.DroneBlast:
            case EventKind.RocketImpact:
            {
                bool drone = e.Kind == EventKind.DroneBlast;
                uint spark = drone ? 0xFFBEE4FFu : 0xFF519FFCu;
                for (int i = 0; i < (drone ? 13 : 8); i++)
                {
                    float a = rng.Range(0, MathF.Tau), d = spread * radius * rng.Range(0.3f, 0.9f);
                    float dx = MathF.Cos(a), dy = MathF.Sin(a);
                    float fx = x + dx * d, fy = y + dy * d;
                    color.Line(fx, fy, fx - dx * fade * 9, fy - dy * fade * 9, spark);
                    if (drone) color.Disc(fx, fy, fade * 2.2f, 0xFF555477u);
                }
                light.Glow(x, y, 24, spark, fade);
                break;
            }
            case EventKind.Laser:
                if (t < 0.8f)
                {
                    float offset = 2 * fade;
                    color.Line(x, y - 10 - offset, e.X2, e.Y2 - 6 - offset, 0xFF6742D9u);
                    color.Line(x, y - 10 + offset, e.X2, e.Y2 - 6 + offset, 0xFF6742D9u);
                    color.Line(x, y - 10, e.X2, e.Y2 - 6, 0xFFD7D5FFu);
                    light.Glow(e.X2, e.Y2 - 6, 16, 0xFF6E65FFu, fade);
                }
                break;
            case EventKind.Rifle:
            {
                var origin = new Vector2(x, y - 10);
                var target = new Vector2(e.X2, e.Y2 - 6);
                var head = Vector2.Lerp(origin, target, Math.Min(1, t * 2.5f));
                var tail = Vector2.Lerp(origin, target, Math.Max(0, t * 1.5f - 0.45f));
                color.Line(tail.X, tail.Y, head.X, head.Y, 0xFFFAF0AAu);
                light.Glow(head.X, head.Y, 13, 0xFFFBE4A1u, fade);
                break;
            }
            case EventKind.CaneStrike:
            {
                float top = y - 8 - fade * fade * 62;
                color.Line(x - 5, top, x + 4, top - 3, 0xFFE8B8DBu);
                color.Line(x + 4, top - 3, x + 6, top + 18 * fade, 0xFF915594u);
                light.Glow(x, top, 14, 0xFFC480C6u, fade);
                break;
            }
            case EventKind.Revival:
            {
                float peak = 1 - MathF.Abs(t * 2 - 1);
                for (int i = 0; i < 22; i++)
                {
                    float a = i * 2.399963f + t * 3;
                    float d = (7 + 36 * spread) * rng.Range(0.35f, 1);
                    float fx = x + MathF.Cos(a) * d;
                    float fy = y + MathF.Sin(a) * d * 0.5f - t * rng.Range(16, 62);
                    color.AlphaDisc(fx, fy, 2 * fade, i % 2 == 0 ? RevivalMint : RevivalGold, fade);
                }
                float top = y - 6 - 66 * spread;
                for (int i = -2; i <= 2; i++)
                    color.Line(x + i * 2, top + Math.Abs(i) * 6, x + i, y - 8 - t * 20,
                        i == 0 ? RevivalGold : RevivalMint);
                light.Glow(x, y - 16, 32, RevivalMint, peak * 1.7f);
                break;
            }
        }
    }
}
