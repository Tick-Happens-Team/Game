using System.Diagnostics;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Platform;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

// TickHappens single-player prototype.
//   --demo              autopilot plays (deterministic, fixed 60 FPS frame time)
//   --frames N          exit after N frames      --screenshot PATH   save the last frame (needs --frames)
//   --seed S            run seed                 --no-vsync          uncapped frame rate
//   --hold-levelup      with --demo: stop at the first level-up screen (for screenshots)
bool demo = false, noVsync = false, holdLevelUp = false;
int frames = 0;
uint seed = 1;
string? screenshot = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--demo": demo = true; break;
        case "--no-vsync": noVsync = true; break;
        case "--hold-levelup": holdLevelUp = true; break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out frames) && frames > 0: i++; break;
        case "--screenshot" when i + 1 < args.Length: screenshot = args[++i]; break;
        case "--seed" when i + 1 < args.Length && uint.TryParse(args[i + 1], out seed): i++; break;
        default:
            Console.Error.WriteLine("Usage: TickHappens.Game [--demo [--hold-levelup]] [--seed S] [--no-vsync] [--frames N [--screenshot PATH]]");
            return 2;
    }
}
if (screenshot is not null && frames == 0)
{
    Console.Error.WriteLine("--screenshot requires --frames.");
    return 2;
}

ContentSet content;
try { content = ContentLoader.Load(ContentLoader.FindCore()); }
catch (ContentException e) { Console.Error.WriteLine(e.Message); return 3; }

var atlas = AtlasData.Load(AssetPaths.Get("atlas", "main.json"));
using var window = new GameWindow("TickHappens", 1280, 720, vsync: !demo && !noVsync);
using var view = new RunView(window.Gl, atlas, content);
var world = new RunWorld(content, seed);
var loop = new RunLoop(world);
var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds, fpsTimer = 0;
int fpsFrames = 0;

for (int frame = 1; !window.QuitRequested; frame++)
{
    window.PumpEvents();
    if (window.QuitRequested || window.WasPressed(Key.Escape)) break;
    if (window.WasPressed(Key.R) && (world.Phase == RunPhase.Dead || loop.Paused))
    {
        world = new RunWorld(content, ++seed);
        loop = new RunLoop(world);
        view.Reset();
    }
    if (window.WasPressed(Key.P) && world.Phase == RunPhase.Playing) loop.TogglePause();

    RunInput input;
    if (demo) input = holdLevelUp && world.Phase == RunPhase.LevelUp ? default : DemoPilot.Decide(world);
    else
    {
        int choice = window.WasPressed(Key.D1) ? 1 : window.WasPressed(Key.D2) ? 2 : window.WasPressed(Key.D3) ? 3 : 0;
        input = new RunInput(Axis(Key.D, Key.Right, Key.A, Key.Left), Axis(Key.S, Key.Down, Key.W, Key.Up),
            window.IsDown(Key.Space), choice);
    }

    double now = clock.Elapsed.TotalSeconds;
    float frameDt = demo ? 1f / 60 : (float)Math.Min(0.1, now - last);
    last = now;
    loop.Advance(frameDt, input);
    view.Consume(world);
    world.ClearEvents();
    view.Draw(window.Width, window.Height, world, world.Phase == RunPhase.Playing ? loop.Interpolation : 1, loop.Paused || world.Phase != RunPhase.Playing, frameDt);

    fpsFrames++;
    if (now - fpsTimer > 0.5)
    {
        window.SetTitle($"TickHappens | {fpsFrames / (now - fpsTimer):F0} fps | enemies {world.AliveEnemies} | WASD move, SPACE dash, 1-3 choose, P pause, R restart, ESC quit");
        fpsTimer = now;
        fpsFrames = 0;
    }
    if (frame == frames)
    {
        if (screenshot is not null) view.SaveScreenshot(screenshot);
        window.Present();
        var p = world.Player;
        Console.WriteLine($"tick {world.Tick} run {world.RunSeconds:F0}s phase {world.Phase} level {p.Level} hp {p.Hp:F0} kills {p.Kills} enemies {world.AliveEnemies} gold {p.Wallet.Gold} weapons {p.Weapons.Count} items {p.Items.Count}");
        break;
    }
    window.Present();
}
return 0;

int Axis(Key positive, Key positiveArrow, Key negative, Key negativeArrow) =>
    (window.IsDown(positive) || window.IsDown(positiveArrow) ? 1 : 0) -
    (window.IsDown(negative) || window.IsDown(negativeArrow) ? 1 : 0);
