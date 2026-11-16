using System.Diagnostics;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Graphics;
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
//   --character ID      start directly with this character (e.g. octarine, swordsman, smith, goflin, celestial_mage, watchman, nekochan)
//   --menu              with --frames: stay on the character select screen (for screenshots)
//   --frames N          exit after N frames      --screenshot PATH   save the last frame (needs --frames)
//   --seed S            run seed                 --no-vsync          uncapped frame rate
//   --dash-every T      demo: also dash every T ticks (VFX captures)
//   --no-audio          stay silent              --record-audio PATH mix the run's sound into a WAV instead of a device
bool demo = false, noVsync = false, stayInMenu = false, noAudio = false;
int frames = 0, dashEvery = 0;
uint seed = 1;
bool seedSpecified = false;
string? screenshot = null, characterId = null, recordAudio = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--demo": demo = true; break;
        case "--no-vsync": noVsync = true; break;
        case "--menu": stayInMenu = true; break;
        case "--no-audio": noAudio = true; break;
        case "--dash-every" when i + 1 < args.Length && int.TryParse(args[i + 1], out dashEvery) && dashEvery > 0: i++; break;
        case "--record-audio" when i + 1 < args.Length: recordAudio = args[++i]; break;
        case "--character" when i + 1 < args.Length: characterId = args[++i]; break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out frames) && frames > 0: i++; break;
        case "--screenshot" when i + 1 < args.Length: screenshot = args[++i]; break;
        case "--seed" when i + 1 < args.Length && uint.TryParse(args[i + 1], out seed): seedSpecified = true; i++; break;
        default:
            Console.Error.WriteLine("Usage: TickHappens.Game [--demo] [--character ID] [--menu] [--seed S] [--no-vsync] [--no-audio] [--record-audio PATH] [--frames N [--screenshot PATH]] | --sim-bench CSV | --net-bench CSV");
            return 2;
    }
}
if (screenshot is not null && frames == 0)
{
    Console.Error.WriteLine("--screenshot requires --frames.");
    return 2;
}

bool randomizeSeed = !seedSpecified && !demo && frames == 0;
static uint NewRunSeed() => (uint)Random.Shared.NextInt64(1, 1L << 32);
if (randomizeSeed) seed = NewRunSeed();

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
var settings = GameSettings.Load();
using var window = new GameWindow(Localizer.Get("app.title"), 1280, 720, vsync: !demo && !noVsync);
using var view = new RunView(window.Gl, atlas, content, settings);
// Automated captures (--frames) stay off the speakers unless they record the mix.
using var audio = new GameAudio(!noAudio && frames == 0, noAudio ? null : recordAudio);
audio.ApplyVolumes(settings);
var settingsMenu = new SettingsMenu(settings, audio);
// The menu shows the selected hero standing in a quiet arena; the run starts from a fresh world.
bool inMenu = stayInMenu || (!demo && characterId == null);
var world = new RunWorld(content, seed, content.Characters[selected].Id);
var loop = new RunLoop(world);
var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds, fpsTimer = 0;
int fpsFrames = 0;
using var online = new OnlineGame(content);
var onlineMenu = new OnlineMenu(online, view.Preview);
var coopHud = new CoopHud(online, settingsMenu);
var itemPanel = new ItemPanel(online.PlayerName);
var pauseMenu = new PauseMenu(settingsMenu);
Action<PixelRenderer> drawPauseMenu = renderer => pauseMenu.Draw(renderer);
Action<PixelRenderer> drawSettingsMenu = settingsMenu.Draw;
Action<PixelRenderer> drawOnlineMenu = renderer => onlineMenu.Draw(renderer, (float)clock.Elapsed.TotalSeconds, window.Mouse);
Action<PixelRenderer> drawHeroMenu = renderer =>
{
    if (!stayInMenu) drawOnlineMenu(renderer);
    settingsMenu.DrawEntry(renderer);
};
bool mainSettingsOpen = false;
bool wasOnlinePlaying = false;
if (inMenu) audio.MenuMusic(); else audio.RunMusic();
bool audioInRun = !inMenu;

for (int frame = 1; !window.QuitRequested; frame++)
{
    window.PumpEvents();
    if (window.QuitRequested) break;
    double now = clock.Elapsed.TotalSeconds;
    float frameDt = demo ? 1f / 60 : (float)Math.Min(0.1, now - last);
    last = now;

    bool onlineMenuWasOpen = onlineMenu.IsOpen;
    bool openedMainSettings = false;
    if (inMenu && online.Playing && online.Interrupted && window.WasPressed(Key.Escape)) online.Leave();
    else if (inMenu && online.Playing && !(online.CanReconnect && window.WasPressed(Key.F7)))
        coopHud.Read(window, view.Renderer);
    else if (inMenu && online.CanReconnect && window.WasPressed(Key.F7)) online.Reconnect();
    else if (inMenu && !mainSettingsOpen && !onlineMenuWasOpen && !online.Playing &&
        settingsMenu.EntryPressed(window, view.Renderer))
    {
        settingsMenu.Open(window);
        mainSettingsOpen = true;
        openedMainSettings = true;
    }
    else if (inMenu && !mainSettingsOpen && !stayInMenu && !online.Playing)
        onlineMenu.Read(window, view.Renderer, content.Characters[selected].Id, seed);
    var netInput = settings.ReadInput(window);
    online.Update(frameDt, coopHud.CapturesInput ? default : netInput);
    if (online.Playing != wasOnlinePlaying)
    {
        if (randomizeSeed && online.Playing) seed = NewRunSeed();
        view.Reset();
        coopHud.Reset();
        onlineMenu.EndNicknameInput(window);
        wasOnlinePlaying = online.Playing;
    }
    if (inMenu && online.Playing)
    {
        var onlineWorld = online.World!;
        itemPanel.Read(window, !coopHud.CapturesInput && !online.Paused && !online.Interrupted);
        view.Consume(onlineWorld);
        audio.Consume(onlineWorld, onlineWorld.Player.Pos);
        onlineWorld.ClearEvents();
        view.Draw(window.Width, window.Height, onlineWorld, online.Alpha, online.Paused, frameDt, online: true,
            interruption: online.Interrupted ? online.Status : null, canReconnect: online.CanReconnect,
            localAlpha: online.LocalAlpha, localCorrection: online.ViewCorrection, coopHud: coopHud, itemPanel: itemPanel);
    }
    else if (inMenu && mainSettingsOpen)
    {
        // The click or key that opens settings must not also activate a settings row.
        if (!openedMainSettings && settingsMenu.Read(window, view.Renderer)) mainSettingsOpen = false;
        view.Draw(window.Width, window.Height, world, 1, true, frameDt, selected,
            mainSettingsOpen ? drawSettingsMenu : drawHeroMenu);
    }
    else if (inMenu && (onlineMenu.IsOpen || onlineMenuWasOpen))
    {
        view.Draw(window.Width, window.Height, world, 1, true, frameDt, selected,
            onlineMenu.IsOpen ? drawOnlineMenu : drawHeroMenu, showHeroMenu: !onlineMenu.IsOpen);
    }
    else if (inMenu)
    {
        if (window.WasPressed(Key.F4))
        {
            string next = Localizer.Languages.First(language => language != Localizer.Language);
            if (!Localizer.Select(next)) Console.Error.WriteLine(Localizer.Get("language.saveError"));
        }
        int delta = (window.WasPressed(Key.S) || window.WasPressed(Key.Down) || window.WasPressed(Key.Right) ? 1 : 0)
                  - (window.WasPressed(Key.W) || window.WasPressed(Key.Up) || window.WasPressed(Key.Left) ? 1 : 0);
        int clicked = Hud.MenuRowAt(view.Renderer, window.LeftClick, content.Characters.Count, selected);
        int page = Hud.MenuPageAt(view.Renderer, window.LeftClick, content.Characters.Count, selected);
        if (clicked >= 0) delta = clicked - selected;
        if (page >= 0) delta = page - selected;
        if (delta != 0)
        {
            audio.Play("ui_move", 0.5f, 0, 0, 2);
            selected = (selected + delta + content.Characters.Count) % content.Characters.Count;
            world = new RunWorld(content, seed, content.Characters[selected].Id);
            loop = new RunLoop(world);
        }
        if (!stayInMenu && (window.WasPressed(Key.Space) || window.WasPressed(Key.Return) || Hud.MenuStartAt(view.Renderer, window.LeftClick)))
        {
            world = new RunWorld(content, seed, content.Characters[selected].Id);
            loop = new RunLoop(world);
            view.Reset();
            inMenu = false;
            audio.Play("ui_confirm", 0.6f, 0, 0, 1);
        }
        view.Draw(window.Width, window.Height, world, 1, paused: true, frameDt, menuSelection: selected,
            overlay: drawHeroMenu, menuMouse: window.Mouse);
    }
    else
    {
        bool ended = world.Phase == RunPhase.Dead;
        bool toHeroSelect = ended
            ? window.WasPressed(Key.R) || window.WasPressed(Key.Escape)
            : pauseMenu.Read(window, view.Renderer);
        if (toHeroSelect)
        {
            seed = randomizeSeed ? NewRunSeed() : seed + 1;
            world = new RunWorld(content, seed, content.Characters[selected].Id);
            loop = new RunLoop(world);
            pauseMenu.Reset();
            view.Reset();
            inMenu = !demo;
            if (demo) audio.RunMusic();
        }

        if (inMenu)
            view.Draw(window.Width, window.Height, world, 1, paused: true, frameDt, menuSelection: selected, overlay: drawHeroMenu,
                menuMouse: window.Mouse);
        else
        {
            if (loop.Paused != pauseMenu.IsOpen) loop.TogglePause();
            var input = demo ? DemoPilot.Decide(world) with { Dash = DemoPilot.Decide(world).Dash || (dashEvery > 0 && world.Tick % dashEvery == 0) }
                : netInput;
            loop.Advance(frameDt, input);
            itemPanel.Read(window, !loop.Paused && world.Phase == RunPhase.Playing);
            view.Consume(world);
            audio.Consume(world, world.Player.Pos);
            world.ClearEvents();
            view.Draw(window.Width, window.Height, world, world.Phase == RunPhase.Playing ? loop.Interpolation : 1,
                loop.Paused || world.Phase != RunPhase.Playing, frameDt, overlay: drawPauseMenu, itemPanel: itemPanel);
        }
    }

    // Music follows the screen: hero select (and online lobby) or a run, local or online.
    bool runNow = !inMenu || online.Playing;
    if (runNow != audioInRun)
    {
        audioInRun = runNow;
        if (runNow) audio.RunMusic(); else audio.MenuMusic();
    }
    bool pauseMusic = online.Playing
        ? !online.Interrupted && online.World!.Phase == RunPhase.Playing && (online.Paused || coopHud.CapturesInput)
        : !inMenu && world.Phase == RunPhase.Playing && loop.Paused;
    audio.SetPauseMusic(pauseMusic);
    audio.Update(frameDt);

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
