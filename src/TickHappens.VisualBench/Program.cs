using System.Diagnostics;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Spatial;
using TickHappens.Engine.Vfx;
using TickHappens.Engine.Vfx.Effects;
using TickHappens.VisualBench;

// Visual test bench: the art pipeline output (atlas + normals + emission), lighting, CPU pixel VFX,
// GPU particles, bloom and a large crowd, rendered through the real engine pipeline.
//   --enemies N   crowd size (default 1500)      --frames N   exit after N frames
//   --screenshot PATH   save the last frame       --no-vsync   uncapped frame rate
//   --demo        scripted showcase (auto-casts on fixed timings, deterministic)
var opt = BenchOptions.Parse(args);
using var window = new GameWindow("TickHappens - visual bench", 1600, 900, vsync: !opt.NoVsync);
var gl = window.Gl;

var atlasData = AtlasData.Load(AssetPaths.Get("atlas", "main.json"));
using var albedo = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main.png")));
using var normals = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_n.png")));
using var emission = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_e.png")));
string shaders = AssetPaths.Get("shaders");
using var renderer = new PixelRenderer(gl, shaders);
using var spriteShader = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "sprite.vert"), Path.Combine(shaders, "sprite.frag"));
using var batch = new SpriteBatch(gl, spriteShader);
using var simShader = ShaderProgram.Compute(gl, Path.Combine(shaders, "particles.comp"));
using var drawShader = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "particles.vert"), Path.Combine(shaders, "particles.frag"));
using var particles = new GpuParticles(gl, simShader, drawShader);

var world = new BenchWorld(atlasData, opt.Enemies, seed: 7, opt.Art);
var effects = new List<IPixelEffect>();
var lights = new List<PointLight>(256);
var rng = new Random(11);
var clock = Stopwatch.StartNew();
var vfxWatch = new Stopwatch();
double last = clock.Elapsed.TotalSeconds, statTimer = 0, time = 0, nextCast = 1.0;
int frames = 0, statFrames = 0;
float shake = 0;
uint effectSeed = 1;
int castIndex = 0;
var chain = new (float X, float Y)[6];

void Shake(float t) => shake = MathF.Max(shake, t);

void ChainLightning()
{
    // Hero → nearest enemy → next nearest from there ... (survivors-style chain), drawn as one multi-hop arc.
    float fx = world.HeroX, fy = world.HeroY - 14;
    int hops = 0;
    Span<(float X, float Y)> one = stackalloc (float X, float Y)[1];
    float cx = fx, cy = fy;
    for (; hops < chain.Length; hops++)
    {
        if (world.Nearest(cx, cy, hops == 0 ? 160 : 80, one, chain.AsSpan(0, hops), minDist: hops == 0 ? 0 : 18) == 0) break;
        chain[hops] = one[0];
        (cx, cy) = (one[0].X, one[0].Y - 6);
    }
    if (hops == 0) return;
    var to = new (float X, float Y)[hops];
    for (int i = 0; i < hops; i++) { to[i] = (chain[i].X * 2, (chain[i].Y - 6) * 2); world.Blast(chain[i].X, chain[i].Y, 6); }
    effects.Add(new LightningEffect(new LightningOptions
    {
        Kind = LightningKind.Arc, From = (fx * 2, fy * 2), To = to, Duration = 0.5f, Seed = effectSeed++, Shake = Shake,
    }));
}

void Slash()
{
    float dir = world.HeroFacingLeft ? -1 : 1;
    float x = world.HeroX + dir * 14, y = world.HeroY - 10;
    effects.Add(new SlashEffect(new SlashOptions { X = x * 2, Y = y * 2, Size = 0.8f, Facing = dir, Seed = effectSeed++, Shake = Shake }));
    world.Blast(x + dir * 10, y, 28);
}

void Impacts()
{
    Span<(float X, float Y)> near = stackalloc (float X, float Y)[5];
    int n = world.Nearest(world.HeroX, world.HeroY, 140, near);
    for (int i = 0; i < n; i++)
    {
        float a = MathF.Atan2(near[i].Y - world.HeroY, near[i].X - world.HeroX);
        effects.Add(new ImpactEffect(new ImpactOptions { X = near[i].X * 2, Y = (near[i].Y - 6) * 2, Angle = a, Size = 0.7f, Seed = effectSeed++ }));
        world.Blast(near[i].X, near[i].Y, 5);
    }
}

void Explode(float x, float y, float size)
{
    effects.Add(new ExplosionEffect(new ExplosionOptions { X = x * 2, Y = y * 2, Size = size, Seed = effectSeed++, Shake = Shake }));
    world.Blast(x, y, 34 * size);
    for (int i = 0; i < 420 * size; i++)
    {
        float a = rng.NextSingle() * MathF.Tau, sp = 60 + rng.NextSingle() * 260 * size;
        particles.Spawn(new GpuParticle
        {
            X = x, Y = y, Vx = MathF.Cos(a) * sp, Vy = MathF.Sin(a) * sp * 0.75f,
            Life = 0.3f + rng.NextSingle() * 0.7f, MaxLife = 1f, Drag = 3.2f, Gravity = 40,
            ColorA = 0xFFA6F3FF, ColorB = 0xFF1D56F2, Intensity = 2.2f, Kind = 0,
        });
    }
}

while (!window.QuitRequested)
{
    window.PumpEvents();
    double now = clock.Elapsed.TotalSeconds;
    float dt = opt.Demo ? 1f / 60f : (float)Math.Min(0.05, now - last);
    last = now;
    time += dt;

    // ---- input & debug toggles ----
    if (window.WasPressed(Key.Escape)) window.RequestQuit();
    var rs = renderer.Settings;
    if (window.WasPressed(Key.F1)) rs.Lighting = !rs.Lighting;
    if (window.WasPressed(Key.F2)) rs.Bloom = !rs.Bloom;
    if (window.WasPressed(Key.F3)) rs.DebugView = (rs.DebugView + 1) % 5;
    if (window.WasPressed(Key.F4)) rs.LightBands = rs.LightBands == 0 ? 6 : 0;
    if (window.WasPressed(Key.Tab)) rs.VfxOpacity = rs.VfxOpacity < 1 ? 1 : 0.55f;
    if (window.WasPressed(Key.N)) world.Night = !world.Night;
    float mx = (window.IsDown(Key.D) || window.IsDown(Key.Right) ? 1 : 0) - (window.IsDown(Key.A) || window.IsDown(Key.Left) ? 1 : 0);
    float my = (window.IsDown(Key.S) || window.IsDown(Key.Down) ? 1 : 0) - (window.IsDown(Key.W) || window.IsDown(Key.Up) ? 1 : 0);
    if (opt.Demo) { mx = MathF.Cos((float)time * 0.4f) * 0.6f; my = MathF.Sin((float)time * 0.4f) * 0.4f; }

    // ---- simulation (bench only; gameplay lives in the game project) ----
    world.Update(dt, mx, my);
    bool auto = (opt.Demo || opt.AutoCast) && time >= nextCast;
    if (auto)
    {
        // Showcase rotation: explosion, chain lightning, slash, impact burst.
        nextCast = time + 0.55;
        switch (castIndex++ % 4)
        {
            case 0: { var (tx, ty) = world.PickTarget(rng); Explode(tx, ty, 0.75f + rng.NextSingle() * 0.5f); break; }
            case 1: ChainLightning(); break;
            case 2: Slash(); break;
            default: Impacts(); break;
        }
    }
    if (window.WasPressed(Key.Space)) { var (tx, ty) = world.PickTarget(rng); Explode(tx, ty, window.IsDown(Key.E) ? 1.6f : 1f); }
    if (window.WasPressed(Key.Q)) Explode(world.HeroX, world.HeroY - 30, 2.2f);
    if (window.WasPressed(Key.D1)) Impacts();
    if (window.WasPressed(Key.D2)) Slash();
    if (window.WasPressed(Key.D3)) ChainLightning();
    if (window.WasPressed(Key.D5) || (opt.Demo && frames == 150))
    {
        // Time statue activation: star-burst over the nearest statue (the gameplay effect - +minutes - lands in P2).
        var (sx2, sy2) = world.NearestStatue();
        effects.Add(new SparkleEffect(new SparkleOptions { X = sx2 * 2, Y = (sy2 - 22) * 2, Seed = effectSeed++ }));
    }
    if (window.WasPressed(Key.D6) || (opt.Demo && frames == 60))
    {
        // XP crystal collected: pops, then the magnet pulls it into the hero.
        effects.Add(new PickupEffect(new PickupOptions
        {
            X = (world.HeroX + 40) * 2, Y = (world.HeroY - 4) * 2, Wait = 0.3f, Seed = effectSeed++,
            Target = () => (world.HeroX * 2, (world.HeroY - 12) * 2),
        }));
    }
    if (window.WasPressed(Key.D4))
    {
        var (tx, ty) = world.PickTarget(rng);
        effects.Add(new LightningEffect(new LightningOptions { Kind = LightningKind.Strike, X = tx * 2, Y = ty * 2, Seed = effectSeed++, Shake = Shake }));
        world.Blast(tx, ty, 40);
    }
    world.EmitAmbient(particles, rng, dt);

    vfxWatch.Restart();
    for (int i = effects.Count - 1; i >= 0; i--)
    {
        effects[i].Update(dt);
        if (effects[i].Done) effects.RemoveAt(i);
    }
    shake = MathF.Max(0, shake - dt * 1.8f);
    float sx = (rng.NextSingle() - 0.5f) * shake * shake * 10, sy = (rng.NextSingle() - 0.5f) * shake * shake * 10;

    // ---- render ----
    renderer.Settings.AmbientR = world.Night ? 0.22f : 0.95f;
    renderer.Settings.AmbientG = world.Night ? 0.22f : 0.93f;
    renderer.Settings.AmbientB = world.Night ? 0.38f : 0.88f;
    renderer.BeginFrame(window.Width, window.Height, world.HeroX + sx, world.HeroY - 12 + sy);
    var frame = renderer.VfxFrame;
    frame.Clear();
    foreach (var e in effects) e.Draw(frame, VfxLayer.All);
    renderer.UploadVfx();
    vfxWatch.Stop();

    batch.Begin();
    world.Draw(batch, renderer.OriginX, renderer.OriginY, renderer.TargetW, renderer.TargetH, (float)time);
    batch.Flush(albedo, normals, emission, renderer.TargetW, renderer.TargetH);

    lights.Clear();
    world.CollectLights(lights, renderer.OriginX + renderer.TargetW / 2f, renderer.OriginY + renderer.TargetH / 2f, (float)time);
    renderer.Light(lights.ToArray(), (float)time);
    particles.Update(dt);
    renderer.BindLit();
    particles.Draw(renderer.OriginX, renderer.OriginY, renderer.TargetW, renderer.TargetH, (float)time);
    renderer.Finish();

    frames++;
    statFrames++;
    if (clock.Elapsed.TotalSeconds - statTimer > 0.5)
    {
        double span = clock.Elapsed.TotalSeconds - statTimer;
        window.SetTitle($"TickHappens bench | {statFrames / Math.Max(span, 1e-3):F0} fps | vfx {vfxWatch.Elapsed.TotalMilliseconds:F2} ms ({effects.Count}) | " +
                        $"sprites {batch.Sprites} | enemies {world.EnemyCount} | lights {renderer.LightCount} | x{renderer.Scale} {renderer.TargetW}x{renderer.TargetH} | " +
                        $"F1 light F2 bloom F3 view F4 bands N night Tab fx | Space boom 1 impact 2 slash 3 chain 4 strike 5 statue 6 pickup");
        statTimer = clock.Elapsed.TotalSeconds;
        statFrames = 0;
    }

    bool last1 = opt.Frames > 0 && frames >= opt.Frames;
    if (window.WasPressed(Key.F12) || (last1 && opt.Screenshot != null))
    {
        var px = renderer.ReadBackBuffer(out int w, out int h);
        string path = opt.Screenshot ?? Path.Combine(".tmp", "screenshots", $"bench_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        new RgbaImage(w, h, px).Save(path);
        Console.WriteLine($"screenshot -> {path}");
    }
    window.Present();
    if (last1)
    {
        Console.WriteLine($"frames {frames}, avg {frames / clock.Elapsed.TotalSeconds:F1} fps, enemies {world.EnemyCount}, sprites {batch.Sprites}, effects {effects.Count}");
        break;
    }
}
return 0;
