using TickHappens.Localization;
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
    private readonly ShaderProgram _shader, _shadowShader, _sim, _particleDraw;
    private readonly SpriteBatch _batch, _shadows;
    private readonly GpuParticles _particles;
    private readonly Texture2D _albedo, _normals, _emission;
    private readonly AtlasSprite _chest, _grass, _stone, _gem, _statue;
    private readonly AtlasSprite[] _heroSprites;
    private readonly AtlasSprite[] _enemySprites;
    private readonly AtlasSprite?[] _companionSprites;
    private readonly SpriteAnim[] _enemyAnims;
    private readonly uint[] _enemyTint;
    private readonly int _chestOpenFrame;
    private readonly List<IPixelEffect> _effects = [];
    private readonly List<PointLight> _lights = new(64);
    private readonly Hud _hud;
    private readonly Random _cosmetic = new(7);
    private float _shake, _time, _aberration, _flash, _moteDebt;
    private uint _flashColor;
    private Vector2 _camera, _kick;
    private bool _cameraValid;

    /// <summary>A short-lived point light from a combat event (kills, blasts, dashes) that fades out.</summary>
    private struct Flash
    {
        public float X, Y, Radius, R, G, B, Life, MaxLife;
    }

    /// <summary>A dash afterimage: the hero sprite left behind, tinted and glowing, for a fraction of a second.</summary>
    private struct Ghost
    {
        public float X, Y, Age;
        public int Sprite, Frame;
        public bool Flip;
    }

    private readonly List<Flash> _flashes = new(64);
    private readonly List<Ghost> _ghosts = new(64);
    private const float GhostLife = 0.3f;

    private readonly HeroPreview _preview;

    public PixelRenderer Renderer => _renderer;

    public RunView(GL gl, AtlasData atlas, ContentSet content, GameSettings settings)
    {
        AtlasSprite S(string id) => atlas.Sprites.TryGetValue(id, out var s) ? s
            : throw new InvalidDataException($"atlas has no sprite '{id}'. Rebuild Assets with TickHappens.AssetTool -- build.");
        _heroSprites = [.. content.Characters.Select(c => S(c.Sprite))];
        _chest = S("prop_chest");
        _grass = S("tile_grass");
        _stone = S("tile_stone");
        _gem = S("pickup_xpgem");
        _statue = S("prop_timestatue");
        _chestOpenFrame = _chest.FrameNames.IndexOf("open");
        if (_chestOpenFrame < 0) throw new InvalidDataException("The atlas is missing the open chest frame. Rebuild Assets.");
        _enemySprites = [.. content.Enemies.Select(e => S(e.Sprite))];
        _companionSprites = [.. content.Items.Select(item => item.Companion is { } c &&
            atlas.Sprites.TryGetValue(c.Sprite, out var sprite) && sprite.Anims.ContainsKey("move") ? sprite : null)];
        _enemyAnims = [.. content.Enemies.Select((e, i) => _enemySprites[i].Anims.TryGetValue(e.Anim, out var a) ? a
            : throw new InvalidDataException($"sprite '{e.Sprite}' has no animation '{e.Anim}'"))];
        _enemyTint = [.. content.Enemies.Select(e => e.Tint is { } t ? TintFromHex(t) : 0xFFFFFFFFu)];

        var shaders = AssetPaths.Get("shaders");
        var albedo = RgbaImage.Load(AssetPaths.Get("atlas", "main.png"));
        _albedo = Texture2D.FromImage(gl, albedo);
        _preview = new HeroPreview(albedo, content, S);
        _normals = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_n.png")));
        _emission = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_e.png")));
        _shader = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "sprite.vert"), Path.Combine(shaders, "sprite.frag"));
        _sim = ShaderProgram.Compute(gl, Path.Combine(shaders, "particles.comp"));
        _particleDraw = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "particles.vert"), Path.Combine(shaders, "particles.frag"));
        _batch = new SpriteBatch(gl, _shader);
        _shadowShader = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "sprite.vert"), Path.Combine(shaders, "shadow.frag"));
        _shadows = new SpriteBatch(gl, _shadowShader);
        _particles = new GpuParticles(gl, _sim, _particleDraw, 1 << 15);
        _renderer = new PixelRenderer(gl, shaders);
        // A closer camera (at least 426x240 art pixels of world; the UI keeps its 640x360 layout) and a dusk look:
        // cool moonlight with drifting cloud shadows, warm hero light, a split-toned grade and a diorama blur.
        _renderer.MinViewW = 426;
        _renderer.MinViewH = 240;
        var look = _renderer.Settings;
        look.AmbientR = 0.27f;
        look.AmbientG = 0.27f;
        look.AmbientB = 0.42f;
        look.SunR = 0.20f;
        look.SunG = 0.22f;
        look.SunB = 0.33f;
        look.Clouds = 0.5f;
        look.BloomStrength = 1.0f;
        look.BloomThreshold = 1.0f;
        look.Contrast = 1.1f;
        look.Saturation = 1.06f;
        look.Lift = 0.025f;
        look.Split = 0.4f;
        look.ShadowTintR = 0.82f;
        look.ShadowTintG = 0.95f;
        look.ShadowTintB = 1.15f;
        look.HighlightTintR = 1.15f;
        look.HighlightTintG = 1.03f;
        look.HighlightTintB = 0.85f;
        look.Vignette = 0.45f;
        look.TiltShift = 1.4f;
        _hud = new Hud(settings);
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
        _flashes.Clear();
        _ghosts.Clear();
        _aberration = _flash = 0;
        _kick = default;
        _cameraValid = false;
    }

    private void AddFlash(float x, float y, float radius, float r, float g, float b, float life)
    {
        if (_flashes.Count < 40) _flashes.Add(new Flash { X = x, Y = y, Radius = radius, R = r, G = g, B = b, Life = life, MaxLife = life });
    }

    private void ScreenFlash(float amount, uint color)
    {
        if (amount < _flash) return;
        _flash = amount;
        _flashColor = color;
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
                    _hud.DamageNumber(e.X, e.Y - 14, e.Value, (DamageKind)e.A);
                    if (impacts++ < MaxImpactsPerFrame && _effects.Count < MaxCpuEffects && _cosmetic.NextSingle() < 0.35f)
                        _effects.Add(new ImpactEffect(new ImpactOptions { X = e.X * 2, Y = (e.Y - 6) * 2, Size = 0.45f, Seed = e.Seed }));
                    break;
                case EventKind.Kill:
                    Burst(e.X, e.Y - 6, 16, 85, 0xFFE070C0u, 0xFF5C1F4Au, 1.8f);
                    AddFlash(e.X, e.Y - 6, 34, 0.9f, 0.35f, 0.8f, 0.18f);
                    break;
                case EventKind.Blast:
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new ExplosionEffect(new ExplosionOptions { X = e.X * 2, Y = e.Y * 2, Size = MathF.Max(0.5f, e.Value / 40), Seed = e.Seed, Shake = Shake }));
                    Burst(e.X, e.Y, 90, 240, 0xFFA6F3FFu, 0xFF1D56F2u, 2.2f);
                    AddFlash(e.X, e.Y - 8, 150, 1.6f, 0.9f, 0.4f, 0.4f);
                    ScreenFlash(0.08f, 0xFF60C0FFu);
                    break;
                case EventKind.DroneBlast:
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new ExplosionEffect(new ExplosionOptions { X = e.X * 2, Y = e.Y * 2, Size = 0.85f, Seed = e.Seed }));
                    Burst(e.X, e.Y, 22, 90, 0xFFFFE4ACu, 0xFF2855D5u, 1.2f);
                    break;
                case EventKind.Chain:
                {
                    var pts = world.ChainPoints;
                    var to = new (float X, float Y)[e.B - 1];
                    for (int k = 1; k < e.B; k++) to[k - 1] = (pts[e.A + k].X * 2, pts[e.A + k].Y * 2);
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new LightningEffect(new LightningOptions
                        {
                            Kind = LightningKind.Arc, From = (pts[e.A].X * 2, pts[e.A].Y * 2), To = to, Duration = 0.35f, Seed = e.Seed,
                        }));
                    for (int k = 1; k < e.B; k += 2) AddFlash(pts[e.A + k].X, pts[e.A + k].Y - 6, 50, 0.5f, 0.8f, 1.6f, 0.22f);
                    break;
                }
                case EventKind.Melee:
                    if (_effects.Count < MaxCpuEffects)
                    {
                        float halfAngle = e.A * MathF.PI / 360f;
                        _effects.Add(new SlashEffect(new SlashOptions
                        {
                            X = e.X * 2, Y = e.Y * 2, Radius = e.Value * 2, Width = 12, Squash = 1,
                            Tilt = MathF.Atan2(e.Y2, e.X2), From = -halfAngle, To = halfAngle, Seed = e.Seed,
                        }));
                    }
                    break;
                case EventKind.Frost:
                    _hud.Ring(e.X, e.Y, e.Value);
                    RingBurst(e.X, e.Y, e.Value, 0xFFFFE4ACu, 0xFFD58848u);
                    break;
                case EventKind.Flame:
                    for (int k = 0; k < 16; k++)
                    {
                        float angle = MathF.Atan2(e.Y2, e.X2) + (_cosmetic.NextSingle() - 0.5f) * e.A * MathF.PI / 180f;
                        float speed = e.Value * (1 + _cosmetic.NextSingle());
                        _particles.Spawn(new GpuParticle
                        {
                            X = e.X + e.X2 * 8, Y = e.Y + e.Y2 * 8 - 5,
                            Vx = MathF.Cos(angle) * speed, Vy = MathF.Sin(angle) * speed,
                            Life = 0.45f, MaxLife = 0.45f, ColorA = 0xFF40C7FFu, ColorB = 0xFF1936DEu, Intensity = 1.25f,
                        });
                    }
                    break;
                case EventKind.ItemBeam:
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new LightningEffect(new LightningOptions
                        {
                            Kind = LightningKind.Arc, From = (e.X * 2, (e.Y - 10) * 2),
                            To = [(e.X2 * 2, (e.Y2 - 6) * 2)], Duration = 0.23f, Seed = e.Seed,
                        }));
                    break;
                case EventKind.Pulse:
                    _hud.Ring(e.X, e.Y - 4, e.Value);
                    RingBurst(e.X, e.Y - 4, e.Value, 0xFFDEF08Fu, 0xFF6A8A1Fu);
                    break;
                case EventKind.LevelUp:
                    if (_effects.Count < MaxCpuEffects)
                        _effects.Add(new SparkleEffect(new SparkleOptions { X = e.X * 2, Y = (e.Y - 26) * 2, Size = 12, Seed = e.Seed }));
                    RingBurst(e.X, e.Y - 10, 26, 0xFFDEF08Fu, 0xFFC0C43Fu);
                    AddFlash(e.X, e.Y - 16, 110, 1.4f, 1.2f, 0.5f, 0.6f);
                    ScreenFlash(0.12f, 0xFF8FF0DEu);
                    _hud.Notice(Localizer.Format("notice.level", e.Value), 0xFFDEF08Fu);
                    break;
                case EventKind.Statue:
                    _effects.Add(new SparkleEffect(new SparkleOptions { X = e.X * 2, Y = (e.Y - 24) * 2, Seed = e.Seed }));
                    Burst(e.X, e.Y - 20, 160, 160, 0xFF6BE2FFu, 0xFF3DC2FFu, 2.4f);
                    _hud.Notice(Localizer.Format("notice.statue", e.Value), 0xFF6BE2FFu);
                    Shake(0.5f);
                    AddFlash(e.X, e.Y - 20, 160, 0.6f, 1.2f, 1.6f, 0.8f);
                    ScreenFlash(0.18f, 0xFFFFE0A0u);
                    break;
                case EventKind.Chest:
                case EventKind.BossReward:
                    _effects.Add(new SparkleEffect(new SparkleOptions { X = e.X * 2, Y = (e.Y - 12) * 2, Size = 10, Seed = e.Seed }));
                    Burst(e.X, e.Y - 8, 60, 120, 0xFF6BE2FFu, 0xFF2A8AFFu, 2f);
                    if (e.A >= 0 && e.B == world.Player.Id)
                    {
                        var item = world.Content.Items[e.A];
                        _hud.Notice(Localizer.Format(e.Kind == EventKind.Chest ? "notice.chest" : "notice.bossReward", Hud.ItemName(item), Hud.RarityName(item.Rarity)), Hud.RarityColor(item.Rarity));
                    }
                    break;
                case EventKind.PlayerHurt:
                    Shake(0.35f);
                    Burst(e.X, e.Y - 10, 12, 80, 0xFF6060FFu, 0xFF1A1A8Au, 1.6f);
                    _aberration = MathF.Max(_aberration, 1.6f);
                    ScreenFlash(0.1f, 0xFF3030FFu);
                    break;
                case EventKind.Dash:
                {
                    var dash = world.Rules.Dash;
                    _effects.Add(new DashSplashEffect(new DashSplashOptions
                    {
                        X = e.X, Y = e.Y, Dx = e.X2, Dy = e.Y2, Distance = dash.Distance, Seconds = dash.Seconds, Seed = e.Seed,
                    }));
                    // Sparks thrown back from the feet and motes trailing along the path.
                    for (int k = 0; k < 16; k++)
                    {
                        float ang = MathF.Atan2(-e.Y2, -e.X2) + (_cosmetic.NextSingle() - 0.5f) * 2.4f, sp = 60 + 140 * _cosmetic.NextSingle();
                        _particles.Spawn(new GpuParticle
                        {
                            X = e.X, Y = e.Y - 2, Vx = MathF.Cos(ang) * sp, Vy = MathF.Sin(ang) * sp * 0.6f,
                            Life = 0.2f + 0.3f * _cosmetic.NextSingle(), MaxLife = 0.5f, Drag = 5, Gravity = 60,
                            ColorA = 0xFFFFF3A6u, ColorB = 0xFFF2561Du, Intensity = 1.3f, Kind = 0,
                        });
                    }
                    for (int k = 0; k < 18; k++)
                        _particles.Spawn(new GpuParticle
                        {
                            X = e.X + e.X2 * dash.Distance * _cosmetic.NextSingle() + _cosmetic.NextSingle() * 6 - 3,
                            Y = e.Y + e.Y2 * dash.Distance * _cosmetic.NextSingle() - _cosmetic.NextSingle() * 18,
                            Vx = -e.X2 * 30, Vy = -e.Y2 * 30 - 10, Life = 0.45f, MaxLife = 0.45f, Drag = 3,
                            ColorA = 0xFFFFF3A6u, ColorB = 0xFFF2561Du, Intensity = 1.4f, Kind = 1,
                        });
                    AddFlash(e.X, e.Y - 4, 80, 0.3f, 0.7f, 1.0f, 0.35f);
                    AddFlash(e.X + e.X2 * dash.Distance, e.Y + e.Y2 * dash.Distance - 4, 64, 0.25f, 0.55f, 0.85f, 0.45f);
                    if (Vector2.DistanceSquared(new Vector2(e.X, e.Y), world.Player.Pos) < 60 * 60)
                    {
                        _kick += new Vector2(e.X2, e.Y2) * 7;
                        _aberration = MathF.Max(_aberration, 1.1f);
                        Shake(0.22f);
                    }
                    break;
                }
                case EventKind.Elite:
                    _hud.Notice(Localizer.Get("notice.elite"), 0xFFE070C0u);
                    break;
                case EventKind.Pickup:
                    if (e.A == (int)PickupKind.Gold) Burst(e.X, e.Y, 4, 40, 0xFF6BE2FFu, 0xFF2A8AFFu, 1.2f);
                    break;
            }
        }
    }

    private void Shake(float t) => _shake = MathF.Max(_shake, t);

    /// <summary>Fireflies and pollen drifting through the view: slow motes that rise and flicker.</summary>
    private void SpawnMotes(float dt, int ox, int oy, int tw, int th)
    {
        _moteDebt += dt * 16;
        for (; _moteDebt >= 1; _moteDebt--)
        {
            bool firefly = _cosmetic.NextSingle() < 0.55f;
            float life = 2.5f + 2.5f * _cosmetic.NextSingle();
            _particles.Spawn(new GpuParticle
            {
                X = ox + _cosmetic.NextSingle() * tw, Y = oy + _cosmetic.NextSingle() * th,
                Vx = (_cosmetic.NextSingle() - 0.5f) * 10, Vy = -2 - 6 * _cosmetic.NextSingle(),
                Life = life, MaxLife = life, Drag = 0.2f, Gravity = -1.5f,
                ColorA = firefly ? 0xFF8FF8E6u : 0xFFF0D0E8u, ColorB = firefly ? 0xFF2A8A3Du : 0xFF6A3D8Au,
                Intensity = firefly ? 2.6f : 1.1f, Kind = 1,
            });
        }
    }

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

    /// <summary>Draws a frame; with <paramref name="menuSelection"/> ≥ 0 the character select screen is drawn over the scene.</summary>
    public void Draw(int width, int height, RunWorld world, float alpha, bool paused, float frameDt,
        int menuSelection = -1, Action<PixelRenderer>? overlay = null, (float X, float Y)? menuMouse = null, bool online = false, string? interruption = null, bool canReconnect = false,
        float? localAlpha = null, Vector2 localCorrection = default, CoopHud? coopHud = null)
    {
        _time += paused ? 0 : frameDt;
        var p = world.Player;
        var hero = Vector2.Lerp(p.Prev, p.Pos, localAlpha ?? alpha) + localCorrection;
        if (!ReferenceEquals(world, _displayWorld))
        {
            _displayWorld = world;
            Array.Clear(_displayActive);
        }
        for (int slot = 0; slot < world.Players.Count; slot++)
        {
            var player = world.Players[slot];
            Vector2 target = ReferenceEquals(player, p) ? hero : Vector2.Lerp(player.Prev, player.Pos, alpha);
            if (!online || ReferenceEquals(player, p) || !_displayActive[slot] ||
                Vector2.DistanceSquared(_displayPlayers[slot], target) > 48 * 48)
                _displayPlayers[slot] = target;
            else if (!paused)
                _displayPlayers[slot] = Vector2.Lerp(_displayPlayers[slot], target, 1 - MathF.Exp(-frameDt * 24));
            _displayActive[slot] = player.Active;
        }
        _shake = MathF.Max(0, _shake - frameDt * 2.2f);
        float sx = (_cosmetic.NextSingle() - 0.5f) * _shake * _shake * 8, sy = (_cosmetic.NextSingle() - 0.5f) * _shake * _shake * 8;
        // The camera eases after the hero with a little look-ahead, and a dash kicks it forward briefly.
        float ease = paused ? 1 : 1 - MathF.Exp(-frameDt * 7);
        var lead = p.Walking && !paused ? p.Facing * 12 : Vector2.Zero;
        var aim = hero + new Vector2(0, -10) + lead;
        _camera = _cameraValid ? Vector2.Lerp(_camera, aim, ease) : aim;
        _cameraValid = true;
        _kick *= MathF.Exp(-frameDt * 9);
        // Keep the camera inside the arena so the void beyond the edge never shows.
        float arena = world.Rules.ArenaSize, halfW = _renderer.ViewW / 2f, halfH = _renderer.ViewH / 2f;
        float camX = Math.Clamp(_camera.X + _kick.X, halfW, MathF.Max(halfW, arena - halfW));
        float camY = Math.Clamp(_camera.Y + _kick.Y, halfH, MathF.Max(halfH, arena - halfH));
        _aberration = MathF.Max(0, _aberration - frameDt * 5);
        _flash = MathF.Max(0, _flash - frameDt * 0.6f);
        var look = _renderer.Settings;
        look.Aberration = _aberration;
        look.FlashR = (_flashColor & 255) / 255f * _flash;
        look.FlashG = ((_flashColor >> 8) & 255) / 255f * _flash;
        look.FlashB = ((_flashColor >> 16) & 255) / 255f * _flash;
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

        _batch.Flush(_albedo, _normals, _emission, tw, th);

        // Contact shadows under everything that stands on the ground, multiplied into the albedo.
        _renderer.BeginAlbedoMultiply();
        _shadows.Begin();
        for (int i = 0; i < world.EnemyHighWater; i++)
        {
            if (!world.EAlive[i] || !Visible(world.EX[i], world.EY[i])) continue;
            var pos = world.EnemyPos(i, alpha);
            float sw = MathF.Max(6, _enemySprites[world.EDef[i]].W * 0.6f);
            _shadows.DrawRect(pos.X - ox - sw / 2, pos.Y - oy - 2, sw, 5, 0x8C000000u);
        }
        for (int i = 0; i < world.Players.Count; i++)
        {
            var player = world.Players[i];
            if (!player.Active || player.Hp <= 0) continue;
            var position = _displayPlayers[i];
            _shadows.DrawRect(position.X - ox - 7, position.Y - oy - 2, 14, 5, 0x99000000u);
        }
        foreach (var st in world.Statues) if (Visible(st.X, st.Y)) _shadows.DrawRect(st.X - ox - 10, st.Y - oy - 3, 20, 7, 0x99000000u);
        foreach (var ch in world.Chests) if (Visible(ch.X, ch.Y)) _shadows.DrawRect(ch.X - ox - 9, ch.Y - oy - 2, 18, 5, 0x8C000000u);
        _shadows.Flush(_albedo, _normals, _emission, tw, th);
        _renderer.EndAlbedoMultiply();

        // Dash afterimages: recorded while a hero dashes, drawn glowing cyan and thinned out as they age.
        float gdt = paused ? 0 : frameDt;
        for (int i = _ghosts.Count - 1; i >= 0; i--)
        {
            var g = _ghosts[i];
            g.Age += gdt;
            if (g.Age >= GhostLife) _ghosts.RemoveAt(i);
            else _ghosts[i] = g;
        }
        for (int i = 0; i < world.Players.Count; i++)
        {
            var player = world.Players[i];
            if (paused || !player.Active || player.DashTicks <= 0 || _ghosts.Count >= _ghosts.Capacity) continue;
            int spriteIndex = world.Content.CharacterIndex(player.Character.Id);
            var sprite = _heroSprites[spriteIndex];
            var anim = sprite.Anims[sprite.Anims.ContainsKey("walk") ? "walk" : "idle"];
            _ghosts.Add(new Ghost
            {
                X = _displayPlayers[i].X, Y = _displayPlayers[i].Y, Sprite = spriteIndex, Flip = player.FacingLeft,
                Frame = anim.Frames[(int)(_time * anim.Fps) % anim.Frames.Count],
            });
        }
        _batch.Begin();
        for (int i = 0; i < _ghosts.Count; i++)
        {
            var g = _ghosts[i];
            float k = g.Age / GhostLife;
            if (k > 0.5f && (i + (int)(_time * 30)) % 2 == 0) continue;  // flicker out
            uint tint = k < 0.12f ? 0xD8FFF0C0u : 0xFFFFD070u;
            _batch.Draw(_heroSprites[g.Sprite], g.Frame, g.X - ox, g.Y - oy, g.Flip, tint, (byte)(70 * (1 - k)));
        }

        // Actors and props sorted by ground Y (painter's order).
        var order = _order;
        int n = 0;
        void Add(int id, float y) { if (n < order.Length) { order[n] = id; _depth[n++] = y; } }
        for (int i = 0; i < world.EnemyHighWater; i++)
            if (world.EAlive[i] && Visible(world.EX[i], world.EY[i])) Add(i, world.EY[i]);
        const int HeroBase = -1, StatueBase = -1000, ChestBase = -2000, CompanionBase = -3000, IllusionBase = -4000;
        for (int i = 0; i < world.Players.Count; i++)
        {
            var player = world.Players[i];
            var position = _displayPlayers[i];
            if (player.Active && Visible(position.X, position.Y)) Add(HeroBase - i, position.Y);
            if (player.Active && player.Hp > 0 && player.IllusionUntilTick > world.Tick &&
                Visible(player.IllusionPosition.X, player.IllusionPosition.Y))
                Add(IllusionBase - i, player.IllusionPosition.Y);
        }
        for (int i = 0; i < world.Statues.Length; i++) if (Visible(world.Statues[i].X, world.Statues[i].Y)) Add(StatueBase - i, world.Statues[i].Y);
        for (int i = 0; i < world.Chests.Length; i++) if (Visible(world.Chests[i].X, world.Chests[i].Y)) Add(ChestBase - i, world.Chests[i].Y);
        var companions = world.Companions;
        for (int i = 0; i < companions.Length; i++)
            if (companions[i].Active && Visible(companions[i].Pos.X, companions[i].Pos.Y)) Add(CompanionBase - i, companions[i].Pos.Y);
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
            else if (id > StatueBase)
            {
                var player = world.Players[HeroBase - id];
                bool blink = player.InvulnTicks > 0 && player.DashTicks == 0 && (world.Tick / 2) % 2 == 0;
                // The character screen shows the hero enlarged on its own stage instead.
                if (blink || player.Hp <= 0 || menuSelection >= 0) continue;
                var sprite = _heroSprites[world.Content.CharacterIndex(player.Character.Id)];
                var anim = sprite.Anims[player.Walking && sprite.Anims.ContainsKey("walk") ? "walk" : "idle"];
                int frame = anim.Frames[(int)(_time * anim.Fps) % anim.Frames.Count];
                var position = _displayPlayers[player.Id];
                _batch.Draw(sprite, frame, position.X - ox, position.Y - oy, player.FacingLeft,
                    tint: ReferenceEquals(player, p) ? 0xFFFFFFFFu : 0xFFE6F4BCu);
            }
            else if (id <= IllusionBase)
            {
                var owner = world.Players[IllusionBase - id];
                var sprite = _heroSprites[world.Content.CharacterIndex(owner.Character.Id)];
                var anim = sprite.Anims["idle"];
                int frame = anim.Frames[(int)(_time * anim.Fps) % anim.Frames.Count];
                _batch.Draw(sprite, frame, owner.IllusionPosition.X - ox, owner.IllusionPosition.Y - oy,
                    owner.FacingLeft, tint: 0xFFC69999u, emissionBoost: 40);
            }
            else if (id <= CompanionBase)
            {
                ref readonly var ally = ref companions[CompanionBase - id];
                var pos = Vector2.Lerp(ally.Prev, ally.Pos, alpha);
                if (_companionSprites[ally.Item] is { } sprite)
                {
                    var anim = sprite.Anims["move"];
                    int frame = anim.Frames[(int)((_time + ally.Id * 0.13f) * anim.Fps) % anim.Frames.Count];
                    _batch.Draw(sprite, frame, pos.X - ox, pos.Y - oy, ally.FacingLeft);
                }
                else _batch.Draw(_gem, 0, pos.X - ox, pos.Y - oy);
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
            var style = world.PStyle[i];
            if (style == RunWorld.ProjectileStyle.Bottle)
            {
                color.Disc(px, py, 3, 0xFF22141Au);
                color.Disc(px, py, 2, 0xFFC43F9Bu);
                color.Set(px, py - 3, 0xFFDEF08Fu);
                continue;
            }
            float len = MathF.Sqrt(world.PVx[i] * world.PVx[i] + world.PVy[i] * world.PVy[i]);
            if (len <= 0.01f) continue;
            float ux = world.PVx[i] / len, uy = world.PVy[i] / len;
            if (style is RunWorld.ProjectileStyle.Trident or RunWorld.ProjectileStyle.ReturningTrident)
            {
                color.Line(px - ux * 10, py - uy * 10, px, py, 0xFFE6D1A9u);
                color.Line(px - uy * 3, py + ux * 3, px + uy * 3, py - ux * 3, 0xFFDEF08Fu);
                for (int tine = -1; tine <= 1; tine++)
                    color.Line(px - uy * tine * 3, py + ux * tine * 3,
                        px - uy * tine * 3 + ux * 3, py + ux * tine * 3 + uy * 3, 0xFFEAECF4u);
                continue;
            }
            if (style is RunWorld.ProjectileStyle.Magic or RunWorld.ProjectileStyle.Rocket or RunWorld.ProjectileStyle.Phantom)
            {
                uint core = style switch
                {
                    RunWorld.ProjectileStyle.Magic => 0xFFF3B78Bu,
                    RunWorld.ProjectileStyle.Phantom => 0xFFFF9EADu,
                    _ => 0xFF63C9FFu,
                };
                color.Line(px - ux * 7, py - uy * 7, px, py, core);
                color.Disc(px, py, 2, core);
                light.Glow(px, py, 9, core, 0.75f);
                continue;
            }
            color.Line(px - ux * 5, py - uy * 5, px - ux * 2, py - uy * 2, 0xFFC0C43Fu);
            color.Line(px - ux * 2, py - uy * 2, px, py, 0xFFDEF08Fu);
            color.Set(px, py, 0xFFFFFFFFu);
            light.Glow(px, py, 7, 0xFFDEF08Fu, 0.45f);
        }
        for (int i = 0; i < RunWorld.PickupCapacity; i++)
        {
            if (!world.GAlive[i] || world.GKind[i] == PickupKind.Xp || !Visible(world.GX[i], world.GY[i])) continue;
            if (world.GKind[i] is PickupKind.Heal or PickupKind.Cursed)
            {
                float x = world.GX[i], y = world.GY[i] - 3;
                uint gem = world.GKind[i] == PickupKind.Heal ? 0xFFFFC379u : 0xFFFFA168u;
                color.Disc(x, y, 3, 0xFF392742u);
                color.Line(x - 1, y + 3, x + 1, y - 3, gem);
                color.Set(x, y - 2, 0xFFFFFFFFu);
                light.Glow(x, y, 10, gem, 0.5f);
                continue;
            }
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
        _lights.Add(new PointLight { X = hero.X - 5, Y = hero.Y - 20, Radius = 160, R = 0.9f * flick, G = 0.68f * flick, B = 0.42f * flick, Height = 26 });
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            f.Life -= dt;
            if (f.Life <= 0) { _flashes.RemoveAt(i); continue; }
            _flashes[i] = f;
            float k = f.Life / f.MaxLife;
            k *= k;
            _lights.Add(new PointLight { X = f.X, Y = f.Y, Radius = f.Radius * (0.7f + 0.3f * k), R = f.R * k, G = f.G * k, B = f.B * k, Height = 14 });
        }
        int boltLights = 0;
        for (int i = 0; i < RunWorld.ProjectileCapacity && boltLights < 12; i++)
            if (world.PAlive[i] && Visible(world.PX[i], world.PY[i]))
            {
                _lights.Add(new PointLight { X = world.PX[i], Y = world.PY[i] + 4, Radius = 30, R = 0.9f, G = 0.85f, B = 0.35f, Height = 8 });
                boltLights++;
            }
        _lights.Add(new PointLight { X = hero.X, Y = hero.Y - 16, Radius = 30, R = 0.2f, G = 0.7f, B = 0.65f, Height = 10 });
        if (online)
        {
            for (int i = 0; i < world.Players.Count; i++)
            {
                var player = world.Players[i];
                var position = _displayPlayers[i];
                if (!player.Active || player.Hp <= 0 || ReferenceEquals(player, p) ||
                    !Visible(position.X, position.Y)) continue;
                _lights.Add(new PointLight { X = position.X - 5, Y = position.Y - 20, Radius = 130,
                    R = 1.0f * flick, G = 0.78f * flick, B = 0.52f * flick, Height = 26 });
                _lights.Add(new PointLight { X = position.X, Y = position.Y - 16, Radius = 30,
                    R = 0.2f, G = 0.7f, B = 0.65f, Height = 10 });
            }
        }
        for (int s = 0; s < world.Statues.Length; s++)
            if (Visible(world.Statues[s].X, world.Statues[s].Y))
                _lights.Add(world.StatueActive[s]
                    ? new PointLight { X = world.Statues[s].X, Y = world.Statues[s].Y - 18, Radius = 50, R = 0.35f, G = 0.25f, B = 0.2f, Height = 18 }
                    : new PointLight { X = world.Statues[s].X, Y = world.Statues[s].Y - 18, Radius = 90, R = 1.05f, G = 0.82f, B = 0.35f, Height = 18 });
        for (int c = 0; c < world.Chests.Length; c++)
            if (!world.ChestOpen[c] && Visible(world.Chests[c].X, world.Chests[c].Y))
                _lights.Add(new PointLight { X = world.Chests[c].X, Y = world.Chests[c].Y - 8, Radius = 40, R = 0.5f, G = 0.4f, B = 0.15f, Height = 10 });
        _renderer.Light(_lights.ToArray(), _time);
        SpawnMotes(dt, ox, oy, tw, th);
        _particles.Update(dt);
        _renderer.BindLit();
        _particles.Draw(ox, oy, tw, th, _time);

        if (menuSelection >= 0) _hud.DrawMenu(_renderer, world.Content, menuSelection, _preview, frameDt, menuMouse);
        else
        {
            DrawVisionMask(world, hero);
            _hud.Draw(_renderer, world, hero, frameDt, paused, online, interruption, canReconnect);
        }
        coopHud?.Draw(_renderer, world, _displayPlayers);
        overlay?.Invoke(_renderer);
        _renderer.Finish();
    }

    private void DrawVisionMask(RunWorld world, Vector2 hero)
    {
        var player = world.Player;
        foreach (int item in player.EffectItems)
        {
            var effect = world.Content.Items[item].Effect!;
            if (effect.Kind != "weldingBlock") continue;
            var (hx, hy) = _renderer.WorldToUi(hero.X, hero.Y);
            var ui = PixelCanvas.ColorOf(_renderer.UiFrame);
            var origin = new Vector2(hx, hy);
            var visible = CombatArea.Cone(origin, _renderer.ViewW + _renderer.ViewH, player.Facing, effect.Value);
            for (int y = 0; y < _renderer.ViewH; y++)
                for (int x = 0; x < _renderer.ViewW; x++)
                    if (!visible.Contains(new Vector2(x, y), out float squared) && squared > 12 * 12)
                        ui.Blend(x, y, 0xFF0E0B14u, 0.9f);
            return;
        }
    }

    private readonly int[] _order = new int[4096];
    private readonly Vector2[] _displayPlayers = new Vector2[4];
    private readonly bool[] _displayActive = new bool[4];
    private RunWorld? _displayWorld;
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
        _shadows.Dispose();
        _shader.Dispose();
        _shadowShader.Dispose();
        _sim.Dispose();
        _particleDraw.Dispose();
        _emission.Dispose();
        _normals.Dispose();
        _albedo.Dispose();
    }
}
