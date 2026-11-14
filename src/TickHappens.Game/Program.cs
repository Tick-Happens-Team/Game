using System.Diagnostics;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Platform;
using TickHappens.Game;

bool demo = false;
int frames = 0;
string? screenshot = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--demo":
            demo = true;
            break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out frames) && frames > 0:
            i++;
            break;
        case "--screenshot" when i + 1 < args.Length:
            screenshot = args[++i];
            break;
        default:
            Console.Error.WriteLine("Usage: TickHappens.Game [--demo] [--frames N [--screenshot PATH]]");
            return 2;
    }
}
if (screenshot is not null && frames == 0)
{
    Console.Error.WriteLine("--screenshot requires --frames.");
    return 2;
}

var atlas = AtlasData.Load(AssetPaths.Get("atlas", "main.json"));
using var window = new GameWindow("TickHappens", 1280, 720, vsync: !demo);
using var view = new RunView(window.Gl, atlas);
var world = new RunWorld();
var loop = new RunLoop(world);
var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds;
int shownTick = -1, shownChest = -2, shownCount = -1;
bool shownPause = false;

for (int frame = 1; !window.QuitRequested; frame++)
{
    window.PumpEvents();
    if (window.QuitRequested || window.WasPressed(Key.Escape)) break;
    if (window.WasPressed(Key.R))
    {
        world = new RunWorld();
        loop = new RunLoop(world);
    }
    if (window.WasPressed(Key.P)) loop.TogglePause();

    int x = Axis(Key.D, Key.Right, Key.A, Key.Left);
    int y = Axis(Key.S, Key.Down, Key.W, Key.Up);
    var input = new RunInput(x, y, window.WasPressed(Key.E));
    if (demo)
    {
        input = world.Tick < 14 ? new RunInput(0, -1)
            : world.OpenedCount == 0 ? new RunInput(0, 0, true)
            : new RunInput(world.Tick < 23 ? 1 : 0, 0);
    }

    double now = clock.Elapsed.TotalSeconds;
    loop.Advance(demo ? 1d / 60 : now - last, input);
    last = now;

    int seconds = world.Tick / RunWorld.TicksPerSecond;
    if (seconds != shownTick || world.NearbyChest != shownChest || world.OpenedCount != shownCount || loop.Paused != shownPause)
    {
        string status = loop.Paused ? "Paused | " : world.NearbyChest >= 0 ? "E to open | " : "";
        window.SetTitle($"TickHappens | {status}{seconds / 60:00}:{seconds % 60:00} | Chests {world.OpenedCount}/{world.Chests.Length} | WASD / Arrows, E open, P pause, R restart, Esc quit");
        (shownTick, shownChest, shownCount, shownPause) = (seconds, world.NearbyChest, world.OpenedCount, loop.Paused);
    }

    view.Draw(window.Width, window.Height, world, loop.Interpolation);
    if (frame == frames)
    {
        if (screenshot is not null) view.SaveScreenshot(screenshot);
        window.Present();
        Console.WriteLine($"Ticks: {world.Tick}, chests opened: {world.OpenedCount}/{world.Chests.Length}");
        break;
    }
    window.Present();
}
return 0;

int Axis(Key positive, Key positiveArrow, Key negative, Key negativeArrow) =>
    (window.IsDown(positive) || window.IsDown(positiveArrow) ? 1 : 0) -
    (window.IsDown(negative) || window.IsDown(negativeArrow) ? 1 : 0);
