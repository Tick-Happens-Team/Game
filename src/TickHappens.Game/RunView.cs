using System.Numerics;
using Silk.NET.OpenGL;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Vfx;
using TickHappens.Engine.Vfx.Effects;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

/// <summary>
/// Draws a run: terrain, props, pickups, enemies and the hero through the pixel pipeline, plus cosmetic VFX
/// spawned locally from simulation events (never fed back into the simulation).
/// </summary>
internal sealed class RunView : IDisposable
{
    private const int Tile = 16, MaxCpuEffects = 28, MaxImpactsPerFrame = 4;
    private readonly PixelRenderer _renderer;
    private readonly ShaderProgram _shader, _sim, _particleDraw;
    private readonly SpriteBatch _batch;
    private readonly GpuParticles _particles;
    private readonly Texture2D _albedo, _normals, _emission;
    private readonly AtlasSprite _hero, _chest, _grass, _stone, _gem, _statue;
    private readonly AtlasSprite[] _enemySprites;
    private readonly SpriteAnim[] _enemyAnims;
    private readonly uint[] _enemyTint;
    private readonly int _chestOpenFrame;
    private readonly List<IPixelEffect> _effects = [];
    private readonly List<PointLight> _lights = new(64);
    private readonly Hud _hud;
    private readonly Random _cosmetic = new(7);
    private float _shake, _time;
    private uint _fxSeed = 1;

    public PixelRenderer Renderer => _renderer;

    public RunView(GL gl, AtlasData atlas, ContentSet content)
    {
        AtlasSprite S(string id) => atlas.Sprites.TryGetValue(id, out var s) ? s
            : throw new InvalidDataException($"atlas has no sprite '{id}'. Rebuild Assets with TickHappens.AssetTool -- build.");
        _hero = S(content.Characters[0].Sprite);
        _chest = S("prop_chest");
        _grass = S("tile_grass");
        _stone = S("tile_stone");
        _gem = S("pickup_xpgem");
        _statue = S("prop_timestatue");
        _chestOpenFrame = _chest.FrameNames.IndexOf("open");
        if (_chestOpenFrame < 0) throw new InvalidDataException("The atlas is missing the open chest frame. Rebuild Assets.");
        _enemySprites = [.. content.Enemies.Select(e => S(e.Sprite))];
        _enemyAnims = [.. content.Enemies.Select((e, i) => _enemySprites[i].Anims.TryGetValue(e.Anim, out var a) ? a
            : throw new InvalidDataException($"sprite '{e.Sprite}' has no animation '{e.Anim}'"))];
        _enemyTint = [.. content.Enemies.Select(e => e.Tint is { } t ? TintFromHex(t) : 0xFFFFFFFFu)];

        var shaders = AssetPaths.Get("shaders");
        _albedo = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main.png")));
        _normals = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_n.png")));
        _emission = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_e.png")));
        _shader = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "sprite.vert"), Path.Combine(shaders, "sprite.frag"));
        _sim = ShaderProgram.Compute(gl, Path.Combine(shaders, "particles.comp"));
        _particleDraw = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "particles.vert"), Path.Combine(shaders, "particles.frag"));
        _batch = new SpriteBatch(gl, _shader);
        _particles = new GpuParticles(gl, _sim, _particleDraw, 1 << 15);
        _renderer = new PixelRenderer(gl, shaders);
        _renderer.Settings.AmbientR = 0.42f;
        _renderer.Settings.AmbientG = 0.40f;
        _renderer.Settings.AmbientB = 0.55f;
        _hud = new Hud();
    }

    private static uint TintFromHex(string hex)
    {
        int v = Convert.ToInt32(hex, 16);
        return 0xFF000000u | (uint)((v & 255) << 16) | (uint)(v & 0xff00) | (uint)((v >> 16) & 255);
    }

    public void Reset()
    {
        _effects.Clear();
        _hud.Reset();
        _shake = 0;
    }

    /// <summary>Turns the simulation's events into local cosmetics and HUD notices. Call once per frame, then clear the world's events.</summary>
    public void Consume(RunWorld world)
    {
        int impacts = 0;
        foreach (var e in world.Events)
        {
            switch (e.Kind)
            {
                case EventKind.Hit:
                    _hud.DamageNumber(e.X, e.Y - 14, e.Value);
                    if (impacts++ < MaxImpactsPerFrame && _effects.Count < MaxCpuEffects && _cosmetic.NextSingle() < 0.35f)
                        _effects.Add(new ImpactEffect(new ImpactOptions { X = e.X * 2, Y = (e.Y - 6) * 2, Size = 0.45f, Seed = _fxSeed++ }));
                    break;
                case EventKind.Kill:
                    Burst(e.X, e.Y - 6, 10, 70, 0xFFE070C0u, 0xFF5C1F4Au, 1.4f);
                    break;
                case EventKind.Blast:
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new ExplosionEffect(new ExplosionOptions { X = e.X * 2, Y = e.Y * 2, Size = MathF.Max(0.5f, e.Value / 40), Seed = _fxSeed++, Shake = Shake }));
                    Burst(e.X, e.Y, 90, 240, 0xFFA6F3FFu, 0xFF1D56F2u, 2.2f);
                    break;
                case EventKind.Chain:
                {
                    var pts = world.ChainPoints;
                    var to = new (float X, float Y)[e.B - 1];
                    for (int k = 1; k < e.B; k++) to[k - 1] = (pts[e.A + k].X * 2, pts[e.A + k].Y * 2);
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new LightningEffect(new LightningOptions
                        {
                            Kind = LightningKind.Arc, From = (pts[e.A].X * 2, pts[e.A].Y * 2), To = to, Duration = 0.35f, Seed = _fxSeed++,
                        }));
                    break;
                }
                case EventKind.Pulse:
                    _hud.Ring(e.X, e.Y - 4, e.Value);
                    RingBurst(e.X, e.Y - 4, e.Value, 0xFFDEF08Fu, 0xFF6A8A1Fu);
                    break;
                case EventKind.LevelUp:
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new SparkleEffect(new SparkleOptions { X = e.X * 2, Y = (e.Y - 26) * 2, Size = 12, Seed = _fxSeed++ }));
                    RingBurst(e.X, e.Y - 10, 26, 0xFFDEF08Fu, 0xFFC0C43Fu);
                    break;
                case EventKind.Statue:
                    _effects.Add(new SparkleEffect(new SparkleOptions { X = e.X * 2, Y = (e.Y - 24) * 2, Seed = _fxSeed++ }));
                    Burst(e.X, e.Y - 20, 160, 160, 0xFF6BE2FFu, 0xFF3DC2FFu, 2.4f);
                    _hud.Notice($"TIME +{e.Value:0}:00  -  THE HORDE GROWS", 0xFF6BE2FFu);
                    Shake(0.5f);
                    break;
                case EventKind.Chest:
                    _effects.Add(new SparkleEffect(new SparkleOptions { X = e.X * 2, Y = (e.Y - 12) * 2, Size = 10, Seed = _fxSeed++ }));
                    Burst(e.X, e.Y - 8, 60, 120, 0xFF6BE2FFu, 0xFF2A8AFFu, 2f);
                    if (world.LastChestReward is { } r) _hud.Notice("CHEST: " + world.Describe(r).Split('|')[0].ToUpperInvariant() + " " + world.Describe(r).Split('|')[1], 0xFF6BE2FFu);
                    break;
                case EventKind.PlayerHurt:
                    Shake(0.35f);
                    Burst(e.X, e.Y - 10, 12, 80, 0xFF6060FFu, 0xFF1A1A8Au, 1.6f);
                    break;
                case EventKind.Dash:
                    for (int k = 0; k < 14; k++)
                        _particles.Spawn(new GpuParticle
                        {
                            X = e.X + _cosmetic.NextSingle() * 6 - 3, Y = e.Y - _cosmetic.NextSingle() * 18,
                            Vx = -e.X2 * 40, Vy = -e.Y2 * 40, Life = 0.35f, MaxLife = 0.35f, Drag = 4,
                            ColorA = 0xFFDEF08Fu, ColorB = 0xFF8A561Fu, Intensity = 1.6f, Kind = 1,
                        });
                    break;
                case EventKind.Elite:
                    _hud.Notice("AN ELITE APPROACHES", 0xFFE070C0u);
                    break;
                case EventKind.Pickup:
                    if (e.A == (int)PickupKind.Gold) Burst(e.X, e.Y, 4, 40, 0xFF6BE2FFu, 0xFF2A8AFFu, 1.2f);
                    break;
            }
        }
    }

    private void Shake(float t) => _shake = MathF.Max(_shake, t);

    private void Burst(float x, float y, int n, float speed, uint a, uint b, float intensity)
    {
        for (int i = 0; i < n; i++)
        {
            float ang = _cosmetic.NextSingle() * MathF.Tau, sp = speed * (0.3f + 0.7f * _cosmetic.NextSingle());
            _particles.Spawn(new GpuParticle
            {
                X = x, Y = y, Vx = MathF.Cos(ang) * sp, Vy = MathF.Sin(ang) * sp * 0.75f,
                Life = 0.25f + 0.45f * _cosmetic.NextSingle(), MaxLife = 0.7f, Drag = 3.5f, Gravity = 30,
                ColorA = a, ColorB = b, Intensity = intensity, Kind = 0,
            });
        }
    }

    private void RingBurst(float x, float y, float r, uint a, uint b)
    {
        int n = (int)Math.Clamp(r * 1.2f, 16, 90);
        for (int i = 0; i < n; i++)
        {
            float ang = i / (float)n * MathF.Tau;
            _particles.Spawn(new GpuParticle
            {
                X = x + MathF.Cos(ang) * r * 0.3f, Y = y + MathF.Sin(ang) * r * 0.22f,
                Vx = MathF.Cos(ang) * r * 2.2f, Vy = MathF.Sin(ang) * r * 1.6f,
                Life = 0.32f, MaxLife = 0.32f, Drag = 5, ColorA = a, ColorB = b, Intensity = 1.8f, Kind = 0,
            });
        }
    }

    public void Draw(int width, int height, RunWorld world, float alpha, bool paused, float frameDt)
    {
        _time += paused ? 0 : frameDt;
        var p = world.Player;
        var hero = Vector2.Lerp(p.Prev, p.Pos, alpha);
        _shake = MathF.Max(0, _shake - frameDt * 2.2f);
        float sx = (_cosmetic.NextSingle() - 0.5f) * _shake * _shake * 8, sy = (_cosmetic.NextSingle() - 0.5f) * _shake * _shake * 8;
        // Keep the camera inside the arena so the void beyond the edge never shows.
        float arena = world.Rules.ArenaSize, halfW = _renderer.ViewW / 2f, halfH = _renderer.ViewH / 2f;
        float camX = Math.Clamp(hero.X, halfW, MathF.Max(halfW, arena - halfW));
        float camY = Math.Clamp(hero.Y - 10, halfH, MathF.Max(halfH, arena - halfH));
        _renderer.BeginFrame(width, height, camX + sx, camY + sy);
        int ox = _renderer.OriginX, oy = _renderer.OriginY, tw = _renderer.TargetW, th = _renderer.TargetH;

        _batch.Begin();
        DrawTerrain(world, ox, oy, tw, th);
        bool Visible(float x, float y) => x > ox - 24 && y > oy - 8 && x < ox + tw + 24 && y < oy + th + 40;

        for (int i = 0; i < RunWorld.PickupCapacity; i++)
        {
            if (!world.GAlive[i] || world.GKind[i] != PickupKind.Xp || !Visible(world.GX[i], world.GY[i])) continue;
            _batch.Draw(_gem, _gem.Anims["spin"].Frames[(int)(_time * 6 + i) % 2], world.GX[i] - ox, world.GY[i] - oy);
        }

        // Actors and props sorted by ground Y (painter's order).
        var order = _order;
        int n = 0;
        void Add(int id, float y) { if (n < order.Length) { order[n] = id; _depth[n++] = y; } }
        for (int i = 0; i < world.EnemyHighWater; i++)
            if (world.EAlive[i] && Visible(world.EX[i], world.EY[i])) Add(i, world.EY[i]);
        const int HeroId = -1, StatueBase = -1000, ChestBase = -2000;
        Add(HeroId, hero.Y);
        for (int i = 0; i < world.Statues.Length; i++) if (Visible(world.Statues[i].X, world.Statues[i].Y)) Add(StatueBase - i, world.Statues[i].Y);
        for (int i = 0; i < world.Chests.Length; i++) if (Visible(world.Chests[i].X, world.Chests[i].Y)) Add(ChestBase - i, world.Chests[i].Y);
        Array.Sort(_depth, order, 0, n);
        for (int k = 0; k < n; k++)
        {
            int id = order[k];
            if (id >= 0)
            {
                var pos = world.EnemyPos(id, alpha);
                int def = world.EDef[id];
                var anim = _enemyAnims[def];
                int frame = anim.Frames[(int)((_time + id * 0.37f) * anim.Fps) % anim.Frames.Count];
                uint tint = world.EFlash[id] > 0 ? 0x00FFFFFFu : _enemyTint[def];
                _batch.Draw(_enemySprites[def], frame, pos.X - ox, pos.Y - oy, flipX: pos.X > hero.X, tint: tint);
            }
            else if (id == HeroId)
            {
                bool blink = p.InvulnTicks > 0 && p.DashTicks == 0 && (world.Tick / 2) % 2 == 0;
                if (blink || world.Phase == RunPhase.Dead) continue;
                var anim = _hero.Anims[p.Walking ? "walk" : "idle"];
                int frame = anim.Frames[(int)(_time * anim.Fps) % anim.Frames.Count];
                _batch.Draw(_hero, frame, hero.X - ox, hero.Y - oy, p.FacingLeft);
            }
            else if (id <= ChestBase)
            {
                int c = ChestBase - id;
                _batch.Draw(_chest, world.ChestOpen[c] ? _chestOpenFrame : 0, world.Chests[c].X - ox, world.Chests[c].Y - oy);
            }
            else
            {
                int s = StatueBase - id;
                bool active = world.StatueActive[s];
                _batch.Draw(_statue, active ? 0 : (int)(_time * 2) % 2, world.Statues[s].X - ox, world.Statues[s].Y - oy,
                    tint: active ? 0xFF8A7A7Au : 0xFFFFFFFFu, emissionBoost: active ? (byte)0 : (byte)40);
            }
        }
        _batch.Flush(_albedo, _normals, _emission, tw, th);

        // CPU VFX layer: effects + projectiles + gold + statue progress (world space).
        var vfx = _renderer.VfxFrame;
        vfx.Clear();
        float dt = paused ? 0 : frameDt;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            _effects[i].Update(dt);
            if (_effects[i].Done) _effects.RemoveAt(i);
        }
        foreach (var e in _effects) e.Draw(vfx, VfxLayer.All);
        var color = PixelCanvas.ColorOf(vfx);
        var light = PixelCanvas.LightOf(vfx);
        for (int i = 0; i < RunWorld.ProjectileCapacity; i++)
        {
            if (!world.PAlive[i]) continue;
            float px = world.PX[i] + world.PVx[i] * RunWorld.Dt * (alpha - 1), py = world.PY[i] + world.PVy[i] * RunWorld.Dt * (alpha - 1);
            float len = MathF.Sqrt(world.PVx[i] * world.PVx[i] + world.PVy[i] * world.PVy[i]);
            float ux = world.PVx[i] / len, uy = world.PVy[i] / len;
            color.Line(px - ux * 5, py - uy * 5, px - ux * 2, py - uy * 2, 0xFFC0C43Fu);
            color.Line(px - ux * 2, py - uy * 2, px, py, 0xFFDEF08Fu);
            color.Set(px, py, 0xFFFFFFFFu);
            light.Glow(px, py, 7, 0xFFDEF08Fu, 0.45f);
        }
        for (int i = 0; i < RunWorld.PickupCapacity; i++)
        {
            if (!world.GAlive[i] || world.GKind[i] != PickupKind.Gold || !Visible(world.GX[i], world.GY[i])) continue;
            float gx = world.GX[i], gy = world.GY[i] - 2;
            color.Disc(gx, gy, 2.2f, 0xFF1A5E8Au);
            color.Disc(gx - 0.3f, gy - 0.3f, 1.6f, 0xFF3DC2FFu);
            color.Set(gx - 1, gy - 1, 0xFFA6F3FFu);
            light.Glow(gx, gy, 5, 0xFF3DC2FFu, 0.25f);
        }
        for (int s = 0; s < world.Statues.Length; s++)
        {
            if (world.StatueActive[s] || world.StatueHold[s] <= 0) continue;
            float k = world.StatueHold[s] / world.Rules.Statue.HoldSeconds;
            int seg = (int)(k * 24);
            for (int j = 0; j < seg; j++)
            {
                float a = -MathF.PI / 2 + j / 24f * MathF.Tau;
                color.Set(world.Statues[s].X + MathF.Cos(a) * 12, world.Statues[s].Y - 2 + MathF.Sin(a) * 5, 0xFF8FF0DEu);
            }
            light.Glow(world.Statues[s].X, world.Statues[s].Y - 16, 30, 0xFF8FF0DEu, 0.4f * k);
        }
        _hud.DrawWorldRings(color, frameDt);
        _renderer.UploadVfx();

        _lights.Clear();
        float flick = 0.94f + 0.06f * MathF.Sin(_time * 11) * MathF.Sin(_time * 6.1f);
        _lights.Add(new PointLight { X = hero.X - 5, Y = hero.Y - 20, Radius = 130, R = 1.0f * flick, G = 0.78f * flick, B = 0.52f * flick, Height = 26 });
        _lights.Add(new PointLight { X = hero.X, Y = hero.Y - 16, Radius = 30, R = 0.2f, G = 0.7f, B = 0.65f, Height = 10 });
        for (int s = 0; s < world.Statues.Length; s++)
            if (Visible(world.Statues[s].X, world.Statues[s].Y))
                _lights.Add(world.StatueActive[s]
                    ? new PointLight { X = world.Statues[s].X, Y = world.Statues[s].Y - 18, Radius = 50, R = 0.35f, G = 0.25f, B = 0.2f, Height = 18 }
                    : new PointLight { X = world.Statues[s].X, Y = world.Statues[s].Y - 18, Radius = 90, R = 1.05f, G = 0.82f, B = 0.35f, Height = 18 });
        for (int c = 0; c < world.Chests.Length; c++)
            if (!world.ChestOpen[c] && Visible(world.Chests[c].X, world.Chests[c].Y))
                _lights.Add(new PointLight { X = world.Chests[c].X, Y = world.Chests[c].Y - 8, Radius = 40, R = 0.5f, G = 0.4f, B = 0.15f, Height = 10 });
        _renderer.Light(_lights.ToArray(), _time);
        _particles.Update(dt);
        _renderer.BindLit();
        _particles.Draw(ox, oy, tw, th, _time);

        _hud.Draw(_renderer, world, hero, frameDt, paused);
        _renderer.Finish();
    }

    private readonly int[] _order = new int[4096];
    private readonly float[] _depth = new float[4096];

    private void DrawTerrain(RunWorld world, int ox, int oy, int width, int height)
    {
        int size = world.Rules.ArenaSize / Tile;
        int left = Math.Max(0, ox / Tile), top = Math.Max(0, oy / Tile);
        int right = Math.Min(size - 1, (ox + width - 1) / Tile), bottom = Math.Min(size - 1, (oy + height - 1) / Tile);
        int c = size / 2;
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int dx = x - c, dy = y - c;
                uint h = unchecked((uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu ^ world.Seed * 0xC2B2AE35u);
                h ^= h >> 15;
                bool plaza = dx * dx + dy * dy < 6 * 6;
                bool path = (Math.Abs(dx) <= 1 || Math.Abs(dy) <= 1) && dx * dx + dy * dy < 30 * 30 && (h & 7) != 0;
                var tile = plaza || path ? _stone : _grass;
                _batch.Draw(tile, (int)((h >> 8) % (uint)tile.Frames.Count), x * Tile - ox, y * Tile - oy);
            }
    }

    public void SaveScreenshot(string path)
    {
        var pixels = _renderer.ReadBackBuffer(out int width, out int height);
        new RgbaImage(width, height, pixels).Save(path);
    }

    public void Dispose()
    {
        _particles.Dispose();
        _renderer.Dispose();
        _batch.Dispose();
        _shader.Dispose();
        _sim.Dispose();
        _particleDraw.Dispose();
        _emission.Dispose();
        _normals.Dispose();
        _albedo.Dispose();
    }
}
