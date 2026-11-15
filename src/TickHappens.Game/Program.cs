using System.Diagnostics;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Platform;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;
using TickHappens.Localization;
using TickHappens.Game.Online;

if (args is ["--net-bench", var networkReport])
{
    try
    {
        NetworkBenchmark.Run(ContentLoader.Load(ContentLoader.FindCore()), networkReport);
        return 0;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ContentException or InvalidOperationException)
    {
        Console.Error.WriteLine(error.Message);
        return 3;
    }
}

if (args is ["--sim-bench", var reportPath])
{
    try
    {
        SimulationBenchmark.Run(ContentLoader.Load(ContentLoader.FindCore()), reportPath);
        return 0;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ContentException)
    {
        Console.Error.WriteLine(error.Message);
        return 3;
    }
}

Localizer.Initialize();

#if TICKHAPPENS_EOS
if (args is ["--eos-probe"]) return EosProbe.Run();
#endif

// TickHappens single-player game: character select, then a run.
//   --demo              autopilot plays (deterministic, fixed 60 FPS frame time), skips the menu
//   --character ID      start directly with this character (e.g. octarine, swordsman, smith, goflin, celestial_mage, watchman)
//   --menu              with --frames: stay on the character select screen (for screenshots)
//   --frames N          exit after N frames      --screenshot PATH   save the last frame (needs --frames)
//   --seed S            run seed                 --no-vsync          uncapped frame rate
bool demo = false, noVsync = false, stayInMenu = false;
int frames = 0;
uint seed = 1;
string? screenshot = null, characterId = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--demo": demo = true; break;
        case "--no-vsync": noVsync = true; break;
        case "--menu": stayInMenu = true; break;
        case "--character" when i + 1 < args.Length: characterId = args[++i]; break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out frames) && frames > 0: i++; break;
        case "--screenshot" when i + 1 < args.Length: screenshot = args[++i]; break;
        case "--seed" when i + 1 < args.Length && uint.TryParse(args[i + 1], out seed): i++; break;
        default:
            Console.Error.WriteLine("Usage: TickHappens.Game [--demo] [--character ID] [--menu] [--seed S] [--no-vsync] [--frames N [--screenshot PATH]] | --sim-bench CSV | --net-bench CSV");
            return 2;
    }
}
if (screenshot is not null && frames == 0)
{
    Console.Error.WriteLine("--screenshot requires --frames.");
    return 2;
}

ContentSet content;
try
{
    string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens");
    content = ContentLoader.Load(ContentLoader.FindCore(),
        Environment.GetEnvironmentVariable("TICKHAPPENS_MODS_ROOT") ?? Path.Combine(local, "mods"),
        Environment.GetEnvironmentVariable("TICKHAPPENS_MOD_PROFILE") ?? Path.Combine(local, "enabled-mods.json"));
}
catch (ContentException e) { Console.Error.WriteLine(e.Message); return 3; }
int selected = 0;
if (characterId != null)
{
    selected = content.CharacterIndex(characterId);
    if (selected < 0)
    {
        Console.Error.WriteLine($"Unknown character '{characterId}'. Known: {string.Join(", ", content.Characters.Select(c => c.Id))}");
        return 2;
    }
}

var atlas = AtlasData.Load(AssetPaths.Get("atlas", "main.json"));
using var window = new GameWindow(Localizer.Get("app.title"), 1280, 720, vsync: !demo && !noVsync);
using var view = new RunView(window.Gl, atlas, content);
// The menu shows the selected hero standing in a quiet arena; the run starts from a fresh world.
bool inMenu = stayInMenu || (!demo && characterId == null);
var world = new RunWorld(content, seed, content.Characters[selected].Id);
var loop = new RunLoop(world);
var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds, fpsTimer = 0;
int fpsFrames = 0;
using var online = new OnlineGame(content);
var onlineMenu = new OnlineMenu(online);
bool wasOnlinePlaying = false;

for (int frame = 1; !window.QuitRequested; frame++)
{
    window.PumpEvents();
    if (window.QuitRequested) break;
    double now = clock.Elapsed.TotalSeconds;
    float frameDt = demo ? 1f / 60 : (float)Math.Min(0.1, now - last);
    last = now;

    bool escapeHandled = inMenu && (onlineMenu.IsOpen || online.Playing);
    bool onlineMenuWasOpen = onlineMenu.IsOpen;
    if (inMenu && online.Playing && window.WasPressed(Key.Escape)) online.Leave();
    else if (inMenu && online.CanReconnect && window.WasPressed(Key.F7)) online.Reconnect();
    else if (inMenu && !stayInMenu && !online.Playing)
        onlineMenu.Read(window, view.Renderer, content.Characters[selected].Id, seed);
    var netInput = new RunInput(Axis(Key.D, Key.Right, Key.A, Key.Left),
        Axis(Key.S, Key.Down, Key.W, Key.Up), window.IsDown(Key.Space) || window.WasPressed(Key.Space));
    online.Update(frameDt, netInput);
    if (online.Playing != wasOnlinePlaying)
    {
        view.Reset();
        wasOnlinePlaying = online.Playing;
    }
    if (inMenu && online.Playing)
    {
        var onlineWorld = online.World!;
        view.Consume(onlineWorld);
        onlineWorld.ClearEvents();
        view.Draw(window.Width, window.Height, onlineWorld, online.Alpha, false, frameDt, online: true,
            interruption: online.Interrupted ? online.Status : null, canReconnect: online.CanReconnect, localAlpha: online.LocalAlpha);
    }
    else if (inMenu && (onlineMenu.IsOpen || onlineMenuWasOpen))
    {
        view.Draw(window.Width, window.Height, world, 1, true, frameDt, selected, onlineMenu.Draw);
    }
    else if (window.WasPressed(Key.Escape) && !escapeHandled) break;
    else if (inMenu)
    {
        if (window.WasPressed(Key.F4))
        {
            string next = Localizer.Languages.First(language => language != Localizer.Language);
            if (!Localizer.Select(next)) Console.Error.WriteLine(Localizer.Get("language.saveError"));
        }
        int delta = (window.WasPressed(Key.D) || window.WasPressed(Key.Right) || window.WasPressed(Key.Down) ? 1 : 0)
                  - (window.WasPressed(Key.A) || window.WasPressed(Key.Left) || window.WasPressed(Key.Up) ? 1 : 0);
        if (delta != 0)
        {
            selected = (selected + delta + content.Characters.Count) % content.Characters.Count;
            world = new RunWorld(content, seed, content.Characters[selected].Id);
            loop = new RunLoop(world);
        }
        if (!stayInMenu && (window.WasPressed(Key.Space) || window.WasPressed(Key.Return)))
        {
            world = new RunWorld(content, seed, content.Characters[selected].Id);
            loop = new RunLoop(world);
            view.Reset();
            inMenu = false;
        }
        view.Draw(window.Width, window.Height, world, 1, paused: true, frameDt, menuSelection: selected,
            overlay: stayInMenu ? null : onlineMenu.Draw);
    }
    else
    {
        if (window.WasPressed(Key.R) && (world.Phase == RunPhase.Dead || loop.Paused))
        {
            seed++;
            if (demo) { world = new RunWorld(content, seed, content.Characters[selected].Id); loop = new RunLoop(world); view.Reset(); }
            else { inMenu = true; world = new RunWorld(content, seed, content.Characters[selected].Id); loop = new RunLoop(world); }
        }
        if (window.WasPressed(Key.P) && world.Phase == RunPhase.Playing) loop.TogglePause();

        var input = demo ? DemoPilot.Decide(world)
            : new RunInput(Axis(Key.D, Key.Right, Key.A, Key.Left), Axis(Key.S, Key.Down, Key.W, Key.Up), window.IsDown(Key.Space));
        loop.Advance(frameDt, input);
        view.Consume(world);
        world.ClearEvents();
        view.Draw(window.Width, window.Height, world, world.Phase == RunPhase.Playing ? loop.Interpolation : 1,
            loop.Paused || world.Phase != RunPhase.Playing, frameDt);
    }

    fpsFrames++;
    if (now - fpsTimer > 0.5)
    {
        window.SetTitle(Localizer.Format("app.stats", fpsFrames / (now - fpsTimer), world.AliveEnemies));
        fpsTimer = now;
        fpsFrames = 0;
    }
    if (frame == frames)
    {
        if (screenshot is not null) view.SaveScreenshot(screenshot);
        window.Present();
        var p = world.Player;
        int items = p.ItemStacks.Sum();
        Console.WriteLine($"{p.Character.Id} tick {world.Tick} run {world.RunSeconds:F0}s phase {world.Phase} level {p.Level} hp {p.Hp:F0}/{world.MaxHealth:F0} " +
                          $"dmg {p.Stats[Stat.Damage]:F1} armor {p.Stats[Stat.Armor]:P0} kills {p.Kills} enemies {world.AliveEnemies} gold {p.Wallet.Gold} items {items}");
        break;
    }
    window.Present();
}
return 0;

int Axis(Key positive, Key positiveArrow, Key negative, Key negativeArrow) =>
    (window.IsDown(positive) || window.IsDown(positiveArrow) ? 1 : 0) -
    (window.IsDown(negative) || window.IsDown(negativeArrow) ? 1 : 0);
