using TickHappens.Engine.Assets;
using TickHappens.Engine.Vfx;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Spatial;

namespace TickHappens.VisualBench;

/// <summary>
/// A throwaway scene for visual and performance testing: tiled arena, hero, a crowd that chases the hero
/// with grid-based separation, statues, chests and crystals. Not gameplay code.
/// </summary>
public sealed class BenchWorld
{
    public const int Tile = 16, MapTiles = 128, WorldSize = Tile * MapTiles;

    private readonly AtlasSprite _hero, _bat, _slime, _statue, _chest, _gem, _grass, _stone;
    private readonly byte[] _map = new byte[MapTiles * MapTiles]; // 0-3 grass variants, 4-5 stone
    private readonly Random _rng;
    private readonly SpatialGrid _grid = new(0, 0, WorldSize, WorldSize, 16);

    // Struct-of-arrays crowd.
    private readonly float[] _ex, _ey, _phase, _flash;
    private readonly byte[] _kind;
    private readonly int[] _order;
    private readonly float[] _sortKey;
    private readonly List<(float X, float Y)> _statues = [], _chests = [], _gems = [];

    public float HeroX = WorldSize / 2f, HeroY = WorldSize / 2f;
    public bool Night = true;
    public int EnemyCount => _ex.Length;
    private bool _heroFlip, _heroMoving;
    private float _heroAnim;

    public BenchWorld(AtlasData atlas, int enemies, int seed, string art)
    {
        _rng = new Random(seed);
        // art = "recipe" (procedural recipes, suffix _r) or "pxs" (hand-authored seed sprites) for side-by-side comparison.
        string sfx = art == "pxs" ? "" : "_r";
        _hero = atlas.Sprites["hero_clockwarden" + sfx];
        _bat = atlas.Sprites["enemy_gloombat" + sfx];
        _slime = atlas.Sprites["enemy_amberslime" + sfx];
        _statue = atlas.Sprites["prop_timestatue"];
        _chest = atlas.Sprites["prop_chest"];
        _gem = atlas.Sprites["pickup_xpgem"];
        _grass = atlas.Sprites["tile_grass"];
        _stone = atlas.Sprites["tile_stone"];

        int c = MapTiles / 2;
        for (int y = 0; y < MapTiles; y++)
            for (int x = 0; x < MapTiles; x++)
            {
                int dx = x - c, dy = y - c;
                bool plaza = dx * dx + dy * dy < 7 * 7;
                bool road = (Math.Abs(dx) <= 1 || Math.Abs(dy) <= 1) && dx * dx + dy * dy < 40 * 40;
                double r = _rng.NextDouble();
                _map[y * MapTiles + x] = plaza || (road && r < 0.85)
                    ? (byte)(4 + (r < 0.5 ? 0 : 1))
                    : (byte)(r < 0.55 ? 0 : r < 0.8 ? 1 : r < 0.93 ? 2 : 3);
            }

        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathF.Tau + 0.3f;
            _statues.Add((HeroX + MathF.Cos(a) * 150, HeroY + MathF.Sin(a) * 110));
        }
        _chests.Add((HeroX + 60, HeroY - 40));
        _chests.Add((HeroX - 90, HeroY + 70));
        for (int i = 0; i < 260; i++)
            _gems.Add((HeroX + (float)(_rng.NextDouble() - 0.5) * 900, HeroY + (float)(_rng.NextDouble() - 0.5) * 600));

        _ex = new float[enemies];
        _ey = new float[enemies];
        _phase = new float[enemies];
        _flash = new float[enemies];
        _kind = new byte[enemies];
        _order = new int[enemies + 64];
        _sortKey = new float[enemies + 64];
        for (int i = 0; i < enemies; i++) Respawn(i);
    }

    private void Respawn(int i)
    {
        float a = (float)_rng.NextDouble() * MathF.Tau, d = 180 + (float)_rng.NextDouble() * 260;
        _ex[i] = HeroX + MathF.Cos(a) * d;
        _ey[i] = HeroY + MathF.Sin(a) * d * 0.7f;
        _kind[i] = (byte)(_rng.NextDouble() < 0.7 ? 0 : 1);
        _phase[i] = (float)_rng.NextDouble() * 10;
        _flash[i] = 0;
    }

    private struct Separation(BenchWorld w, int self) : IGridVisitor
    {
        public float Px, Py;
        public void Visit(int j)
        {
            if (j == self) return;
            float dx = w._ex[self] - w._ex[j], dy = w._ey[self] - w._ey[j], d2 = dx * dx + dy * dy;
            if (d2 > 0.01f && d2 < 100) { float d = MathF.Sqrt(d2); Px += dx / d * (10 - d); Py += dy / d * (10 - d); }
        }
    }

    public void Update(float dt, float mx, float my)
    {
        float len = MathF.Sqrt(mx * mx + my * my);
        _heroMoving = len > 0.01f;
        if (_heroMoving)
        {
            HeroX = Math.Clamp(HeroX + mx / len * 72 * dt, 64, WorldSize - 64);
            HeroY = Math.Clamp(HeroY + my / len * 72 * dt, 64, WorldSize - 64);
            if (MathF.Abs(mx) > 0.01f) _heroFlip = mx < 0;
        }
        _heroAnim += dt;

        _grid.Build(_ex, _ey);
        for (int i = 0; i < _ex.Length; i++)
        {
            float dx = HeroX - _ex[i], dy = HeroY - _ey[i], d = MathF.Sqrt(dx * dx + dy * dy);
            if (d > 700) { Respawn(i); continue; }
            float speed = _kind[i] == 0 ? 38 : 20;
            var sep = new Separation(this, i);
            _grid.Query(_ex[i], _ey[i], 10, ref sep);
            if (d > 14) { _ex[i] += dx / d * speed * dt; _ey[i] += dy / d * speed * dt; }
            _ex[i] += sep.Px * 4 * dt;
            _ey[i] += sep.Py * 4 * dt;
            _phase[i] += dt;
            if (_flash[i] > 0 && (_flash[i] -= dt) <= 0) Respawn(i);
        }
    }

    /// <summary>Marks enemies in the radius as hit; they flash white and respawn.</summary>
    public void Blast(float x, float y, float radius)
    {
        for (int i = 0; i < _ex.Length; i++)
        {
            float dx = _ex[i] - x, dy = _ey[i] - y;
            if (dx * dx + dy * dy < radius * radius && _flash[i] <= 0) _flash[i] = 0.12f;
        }
    }

    public (float X, float Y) PickTarget(Random rng)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            int i = rng.Next(_ex.Length);
            float dx = _ex[i] - HeroX, dy = _ey[i] - HeroY;
            if (MathF.Abs(dx) < 260 && MathF.Abs(dy) < 150 && dx * dx + dy * dy > 40 * 40) return (_ex[i], _ey[i]);
        }
        float a = (float)rng.NextDouble() * MathF.Tau;
        return (HeroX + MathF.Cos(a) * 90, HeroY + MathF.Sin(a) * 60);
    }

    public bool HeroFacingLeft => _heroFlip;

    private int HeroFrame()
    {
        var anim = _hero.Anims[_heroMoving ? "walk" : "idle"];
        return anim.Frames[(int)(_heroAnim * anim.Fps) % anim.Frames.Count];
    }

    public ActorSilhouette HeroSilhouette(RgbaImage atlas)
    {
        var uv = _hero.Frames[HeroFrame()];
        return new ActorSilhouette((int)MathF.Floor(HeroX - _hero.Origin[0]),
            (int)MathF.Floor(HeroY - _hero.Origin[1]), _hero.W, _hero.H,
            atlas.Px, atlas.W, uv[1] * atlas.W + uv[0], _heroFlip);
    }

    public (float X, float Y) NearestStatue() =>
        _statues.MinBy(s => (s.X - HeroX) * (s.X - HeroX) + (s.Y - HeroY) * (s.Y - HeroY));

    /// <summary>Up to k nearest enemies to (x, y) within maxDist, nearest first (writes into dst, returns count).</summary>
    public int Nearest(float x, float y, float maxDist, Span<(float X, float Y)> dst, ReadOnlySpan<(float X, float Y)> exclude = default, float minDist = 0)
    {
        int n = 0;
        Span<float> dist = stackalloc float[dst.Length];
        for (int i = 0; i < _ex.Length; i++)
        {
            if (_flash[i] > 0) continue;
            float dx = _ex[i] - x, dy = _ey[i] - y, d2 = dx * dx + dy * dy;
            if (d2 > maxDist * maxDist || d2 < minDist * minDist) continue;
            bool skip = false;
            foreach (var e in exclude) if (MathF.Abs(e.X - _ex[i]) < 0.5f && MathF.Abs(e.Y - _ey[i]) < 0.5f) { skip = true; break; }
            if (skip) continue;
            int pos = n < dst.Length ? n++ : dst.Length;
            if (pos == dst.Length) { if (d2 >= dist[dst.Length - 1]) continue; pos = dst.Length - 1; }
            while (pos > 0 && dist[pos - 1] > d2) { dist[pos] = dist[pos - 1]; dst[pos] = dst[pos - 1]; pos--; }
            dist[pos] = d2;
            dst[pos] = (_ex[i], _ey[i]);
        }
        return n;
    }

    public void EmitAmbient(GpuParticles ps, Random rng, float dt)
    {
        // Fireflies drifting around the view: cheap GPU motes, purely cosmetic.
        for (int i = 0; i < (int)(240 * dt) + (rng.NextDouble() < 240 * dt % 1 ? 1 : 0); i++)
        {
            bool warm = rng.NextDouble() < 0.3;
            ps.Spawn(new GpuParticle
            {
                X = HeroX + (rng.NextSingle() - 0.5f) * 720, Y = HeroY + (rng.NextSingle() - 0.5f) * 420,
                Vx = (rng.NextSingle() - 0.5f) * 14, Vy = -4 - rng.NextSingle() * 10, Life = 2 + rng.NextSingle() * 3, MaxLife = 5,
                Drag = 0.2f, Gravity = 0, ColorA = warm ? 0xFF6BE2FFu : 0xFFDEF08Fu, ColorB = warm ? 0xFF2A8AFFu : 0xFFC0C43Fu,
                Intensity = Night ? 1.6f : 0.6f, Kind = 1,
            });
        }
    }

    public void Draw(SpriteBatch b, int ox, int oy, int tw, int th, float time)
    {
        // Ground (only visible tiles), positions relative to the render target origin.
        int tx0 = Math.Max(0, ox / Tile), ty0 = Math.Max(0, oy / Tile);
        int tx1 = Math.Min(MapTiles - 1, (ox + tw) / Tile), ty1 = Math.Min(MapTiles - 1, (oy + th) / Tile);
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                byte t = _map[ty * MapTiles + tx];
                var s = t >= 4 ? _stone : _grass;
                b.Draw(s, t >= 4 ? t - 4 : t, tx * Tile - ox, ty * Tile - oy);
            }

        foreach (var (x, y) in _gems)
            if (Visible(x, y, ox, oy, tw, th))
                b.Draw(_gem, _gem.Anims["spin"].Frames[(int)(time * 6 + x) % 2], x - ox, y - oy);

        // Actors sorted by feet Y (painter's order).
        int n = 0;
        for (int i = 0; i < _ex.Length; i++)
            if (Visible(_ex[i], _ey[i], ox, oy, tw, th)) { _order[n] = i; _sortKey[n++] = _ey[i]; }
        int baseIdx = _ex.Length;
        void Add(int code, float y) { _order[n] = code; _sortKey[n++] = y; }
        Add(baseIdx, HeroY);
        for (int i = 0; i < _statues.Count; i++) Add(baseIdx + 1 + i, _statues[i].Y);
        for (int i = 0; i < _chests.Count; i++) Add(baseIdx + 32 + i, _chests[i].Y);
        Array.Sort(_sortKey, _order, 0, n);

        for (int k = 0; k < n; k++)
        {
            int id = _order[k];
            if (id < baseIdx)
            {
                var s = _kind[id] == 0 ? _bat : _slime;
                var anim = _kind[id] == 0 ? s.Anims["fly"] : s.Anims["idle"];
                int f = anim.Frames[(int)(_phase[id] * anim.Fps) % anim.Frames.Count];
                uint tint = _flash[id] > 0 ? 0x00FFFFFFu : 0xFFFFFFFFu;
                b.Draw(s, f, _ex[id] - ox, _ey[id] - oy, flipX: _ex[id] > HeroX, tint: tint);
            }
            else if (id == baseIdx)
            {
                b.Draw(_hero, HeroFrame(), HeroX - ox, HeroY - oy, _heroFlip);
            }
            else if (id < baseIdx + 32)
            {
                var (x, y) = _statues[id - baseIdx - 1];
                b.Draw(_statue, (int)(time * 2 + x) % 2, x - ox, y - oy);
            }
            else
            {
                var (x, y) = _chests[id - baseIdx - 32];
                b.Draw(_chest, 0, x - ox, y - oy);
            }
        }
    }

    private static bool Visible(float x, float y, int ox, int oy, int tw, int th) =>
        x > ox - 32 && y > oy - 8 && x < ox + tw + 32 && y < oy + th + 40;

    public void CollectLights(List<PointLight> lights, float cx, float cy, float time)
    {
        float flick = 0.92f + 0.08f * MathF.Sin(time * 13) * MathF.Sin(time * 7.3f);
        // Hero lantern: warm key light slightly above, cool rim from the eyes.
        lights.Add(new PointLight { X = HeroX - 6, Y = HeroY - 22, Radius = 120, R = 1.05f * flick, G = 0.78f * flick, B = 0.5f * flick, Height = 26 });
        lights.Add(new PointLight { X = HeroX, Y = HeroY - 16, Radius = 34, R = 0.2f, G = 0.75f, B = 0.7f, Height = 10 });
        foreach (var (x, y) in _statues)
            if (MathF.Abs(x - cx) < 420 && MathF.Abs(y - cy) < 280)
                lights.Add(new PointLight { X = x, Y = y - 18, Radius = 90, R = 1.1f, G = 0.85f, B = 0.35f, Height = 18 });
        foreach (var (x, y) in _gems)
            if (lights.Count < PixelRenderer.MaxLights && MathF.Abs(x - cx) < 360 && MathF.Abs(y - cy) < 220)
                lights.Add(new PointLight { X = x, Y = y - 4, Radius = 22, R = 0.15f, G = 0.6f, B = 0.55f, Height = 6 });
    }
}
