using System.Diagnostics;
using System.Text.Json;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Vfx;
using TickHappens.Localization;
using static TickHappens.Launcher.Paint;

namespace TickHappens.Launcher;

internal enum Page { Play, Mods, Settings }

internal sealed record LauncherOptions(bool Offline, Page Page, int Frames, string? Screenshot);

/// <summary>
/// The launcher window: a sidebar with pages, a procedural banner, a mods list, settings, and a bottom bar with
/// status, download progress and the play button. Drawn on the CPU into a 640×360 pixel layer with the game's
/// pixel font. Updates and the game process run as background tasks polled once per frame.
/// </summary>
internal sealed class LauncherApp
{
    private const int Sidebar = 128, BarTop = 314;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(15);

    private readonly Updater _updater;
    private readonly LauncherOptions _options;
    private readonly string _root, _modsDirectory, _profile;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(string Id, bool Enabled)> _mods = [];

    private Page _page;
    private volatile string _status = "";
    private volatile bool _statusIsError;
    private Task? _work;
    private volatile bool _gameRunning;
    private bool _closeRequested;
    private volatile string? _game;
    private volatile InstalledRelease? _installed;
    private TimeSpan _nextCheck = TimeSpan.Zero;
    private int _modScroll;
    private (int X, int Y) _mouse = (-1, -1);
    private (int X, int Y)? _click;

    public string? NewLauncher { get; private set; }

    public LauncherApp(Updater updater, string root, LauncherOptions options)
    {
        _updater = updater;
        _options = options;
        _root = root;
        _page = options.Page;
        _modsDirectory = Path.Combine(root, "mods");
        _profile = Path.Combine(root, "enabled-mods.json");
        Directory.CreateDirectory(_modsDirectory);
        ReloadMods();
        RefreshInstalled();
        _status = Localizer.Get(_game is null ? "launcher.noGame" : "launcher.ready");
    }

    private bool Busy => _work is { IsCompleted: false };

    public void Run(GameWindow window, Screen screen)
    {
        int frame = 0;
        while (!window.QuitRequested || Busy)
        {
            window.PumpEvents();
            if (window.QuitRequested && Busy && !_closeRequested)
            {
                // Closing during an install cancels it; a running game keeps the launcher's lease until it exits.
                _closeRequested = true;
                _cancellation.Cancel();
                SDL3.SDL.HideWindow(window.Handle);
            }
            ObserveWork();
            if (NewLauncher is not null) return;
            if (!_options.Offline && !Busy && !_closeRequested && _clock.Elapsed >= _nextCheck)
            {
                _nextCheck = _clock.Elapsed + UpdateInterval;
                Start(UpdateAsync());
            }

            screen.Layout(window.Width, window.Height);
            _mouse = window.Mouse is { } m ? screen.ToScreen(m, window.Width, window.Height) : (-1, -1);
            _click = window.LeftClick is { } l ? screen.ToScreen(l, window.Width, window.Height) : null;
            HandleKeys(window);
            if (window.Wheel != 0 && _page == Page.Mods)
                _modScroll = Math.Clamp(_modScroll - Math.Sign(window.Wheel), 0, Math.Max(0, _mods.Count - VisibleMods));

            bool idle = !Busy;
            Draw(screen.Canvas, (float)_clock.Elapsed.TotalSeconds);
            screen.Present(window.Width, window.Height);
            window.Present();
            frame++;
            if (_options.Screenshot is { } path && frame >= Math.Max(1, _options.Frames) && idle)
            {
                SaveScreenshot(screen.Frame, path);
                return;
            }
            // The launcher idles in the background while the game runs; keep its CPU use low.
            if (_gameRunning || _closeRequested) Thread.Sleep(100);
        }
    }

    private void HandleKeys(GameWindow window)
    {
        if (window.WasPressed(Key.Return) && CanPlay) Start(LaunchAsync());
        if (window.WasPressed(Key.Tab)) _page = (Page)(((int)_page + 1) % 3);
        if (_page == Page.Mods)
        {
            if (window.WasPressed(Key.Down)) _modScroll = Math.Min(_modScroll + 1, Math.Max(0, _mods.Count - VisibleMods));
            if (window.WasPressed(Key.Up)) _modScroll = Math.Max(0, _modScroll - 1);
        }
    }

    private bool CanPlay => !Busy && _game is not null && !_closeRequested;

    private void Start(Task task) => _work = task;

    private void ObserveWork()
    {
        if (_work is not { IsCompleted: true } done) return;
        _work = null;
        if (done.Exception is { } error) SetError(error.InnerException ?? error);
    }

    private async Task UpdateAsync()
    {
        try
        {
            NewLauncher = await Task.Run(() => _updater.UpdateAsync(SetStatus, _cancellation.Token));
            if (NewLauncher is null) SetStatus(Localizer.Get("launcher.current"));
        }
        catch (Exception error) { SetError(error, "launcher.updateError"); }
        if (NewLauncher is null && !_closeRequested) await Task.Run(RefreshInstalled);
    }

    private async Task LaunchAsync()
    {
        try
        {
            string entry = await Task.Run(() => _updater.CurrentEntry("game")) ?? throw new LocalizedException("launcher.noGame");
            var start = new ProcessStartInfo(entry) { WorkingDirectory = Path.GetDirectoryName(entry)! };
            start.Environment["TICKHAPPENS_MODS_ROOT"] = _modsDirectory;
            start.Environment["TICKHAPPENS_MOD_PROFILE"] = _profile;
            start.Environment["TICKHAPPENS_LANGUAGE"] = Localizer.Language;
            using var game = Process.Start(start) ?? throw new IOException("Could not start the game.");
            _gameRunning = true;
            SetStatus(Localizer.Get("launcher.running"));
            // Updates wait until the game exits so the active installation never changes underneath it.
            await game.WaitForExitAsync();
            SetStatus(game.ExitCode == 0 ? Localizer.Get("launcher.ready") : Localizer.Format("launcher.exit", game.ExitCode));
        }
        catch (Exception error) { SetError(error); }
        finally { _gameRunning = false; }
    }

    private void RefreshInstalled()
    {
        try
        {
            _game = _updater.CurrentEntry("game");
            _installed = _game is null ? null : _updater.InstalledVersions();
        }
        catch (Exception error)
        {
            _game = null;
            _installed = null;
            SetError(error);
        }
    }

    private void SetStatus(string text)
    {
        _status = text;
        _statusIsError = false;
    }

    private void SetError(Exception error, string key = "launcher.error")
    {
        _status = ErrorText(error, key);
        _statusIsError = true;
    }

    // ---- Mods ----

    private const int ModRowHeight = 18, ModListTop = 64;
    private static int VisibleMods => (BarTop - 12 - ModListTop) / ModRowHeight;

    private void ReloadMods()
    {
        _mods.Clear();
        try
        {
            var selected = File.Exists(_profile)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(_profile)) ?? []
                : [];
            foreach (string dir in Directory.EnumerateDirectories(_modsDirectory).Order(StringComparer.Ordinal))
                if (File.Exists(Path.Combine(dir, "items.json")))
                {
                    string id = Path.GetFileName(dir);
                    _mods.Add((id, selected.Contains(id, StringComparer.Ordinal)));
                }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { SetError(error); }
        _modScroll = Math.Clamp(_modScroll, 0, Math.Max(0, _mods.Count - VisibleMods));
    }

    private void ToggleMod(int index)
    {
        var (id, enabled) = _mods[index];
        _mods[index] = (id, !enabled);
        try
        {
            string[] selection = [.. _mods.Where(mod => mod.Enabled).Select(mod => mod.Id)];
            File.WriteAllText(_profile + ".new", JsonSerializer.Serialize(selection));
            File.Move(_profile + ".new", _profile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _mods[index] = (id, enabled);
            SetError(error);
        }
    }

    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { SetError(error); }
    }

    // ---- Drawing and widgets ----

    private bool Hover(int x, int y, int w, int h) =>
        _mouse.X >= x && _mouse.X < x + w && _mouse.Y >= y && _mouse.Y < y + h;

    private bool Clicked(int x, int y, int w, int h) =>
        _click is { } p && p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h;

    private bool Button(PixelCanvas c, string text, int x, int y, int w, int h, bool enabled = true, bool selected = false)
    {
        bool hover = enabled && Hover(x, y, w, h);
        uint face = !enabled ? PanelDark : selected ? PanelLight : hover ? Mix(Panel, PanelLight, 0.6f) : Panel;
        Bevel(c, x, y, w, h, face, Mix(face, Text, 0.25f), Mix(face, Ink, 0.5f));
        if (selected) Fill(c, x + 2, y + 2, 2, h - 4, Accent);
        Centered(c, text, x + w / 2, y + (h - 7) / 2, enabled ? Text : Muted);
        return enabled && Clicked(x, y, w, h);
    }

    private void Draw(PixelCanvas c, float time)
    {
        Fill(c, 0, 0, Screen.W, Screen.H, Screen.Backdrop);
        switch (_page)
        {
            case Page.Play: DrawPlay(c, time); break;
            case Page.Mods: DrawMods(c); break;
            case Page.Settings: DrawSettings(c); break;
        }
        DrawSidebar(c, time);
        DrawBar(c, time);
    }

    private void DrawSidebar(PixelCanvas c, float time)
    {
        Fill(c, 0, 0, Sidebar, Screen.H, PanelDark);
        Fill(c, Sidebar - 1, 0, 1, Screen.H, Ink);
        Fill(c, Sidebar - 2, 0, 1, Screen.H, Panel);

        // Logo: a small clock and the stacked title.
        int cx = 22, cy = 26;
        c.Disc(cx, cy, 11, Ink);
        c.Disc(cx, cy, 10, Gold);
        c.Disc(cx, cy, 8, Rgb(0xFF, 0xE9, 0xB8));
        float minute = time * MathF.Tau / 6;
        c.Line(cx, cy, cx + MathF.Sin(minute) * 6, cy - MathF.Cos(minute) * 6, Ink);
        c.Line(cx, cy, cx + 3, cy, Ink);
        Label(c, "TICK", 38, 14, Gold, 2);
        Label(c, "HAPPENS", 38, 30, Text, 1);
        Fill(c, 12, 46, Sidebar - 24, 1, Panel);

        (Page Page, string Key)[] pages = [(Page.Play, "launcher.page.play"), (Page.Mods, "launcher.page.mods"), (Page.Settings, "launcher.page.settings")];
        for (int i = 0; i < pages.Length; i++)
            if (Button(c, Localizer.Get(pages[i].Key), 10, 58 + i * 28, Sidebar - 20, 22, selected: _page == pages[i].Page))
            {
                _page = pages[i].Page;
                if (_page == Page.Mods) ReloadMods();
            }

        Label(c, Localizer.Format("launcher.version", Updater.Version), 12, Screen.H - 16, Muted);
    }

    private void DrawPlay(PixelCanvas c, float time)
    {
        int x0 = Sidebar, w = Screen.W - Sidebar;
        Scenery.Draw(c, x0, 0, w, BarTop, time);

        // Title with a hard drop shadow.
        int tx = x0 + w / 2 + 70;
        PixelFontShadowed(c, "TICK", tx, 82, Gold, 4);
        PixelFontShadowed(c, "HAPPENS", tx, 116, Text, 3);
        Centered(c, Localizer.Get("launcher.tagline"), tx, 146, Accent);

        // Installed release card.
        int cardX = x0 + 12, cardY = 12, cardW = 168, cardH = 46;
        Shade(c, cardX, cardY, cardW, cardH, Ink, 0.72f);
        Fill(c, cardX, cardY, cardW, 1, PanelLight);
        if (_installed is { } v)
        {
            Label(c, Localizer.Format("launcher.installed", v.Game), cardX + 8, cardY + 8, Text);
            Label(c, Localizer.Format("launcher.assetsVersion", v.Assets), cardX + 8, cardY + 20, Muted);
            Label(c, Localizer.Format("launcher.release", v.Sequence), cardX + 8, cardY + 32, Muted);
        }
        else
        {
            Label(c, Localizer.Get("launcher.notInstalled"), cardX + 8, cardY + 8, Text);
            foreach (var (line, i) in Wrap(Localizer.Get("launcher.firstRun"), cardW - 16).Take(2).Select((line, i) => (line, i)))
                Label(c, line, cardX + 8, cardY + 20 + i * 10, Muted);
        }
    }

    private static void PixelFontShadowed(PixelCanvas c, string text, int cx, int y, uint color, int scale)
    {
        int x = cx - Measure(text, scale) / 2;
        TickHappens.Engine.Ui.PixelFont.Draw(c, text, x + scale, y + scale, Ink, 0, scale);
        TickHappens.Engine.Ui.PixelFont.Draw(c, text, x, y, color, 0, scale);
    }

    private void DrawMods(PixelCanvas c)
    {
        int x0 = Sidebar + 16, w = Screen.W - Sidebar - 32;
        Label(c, Localizer.Get("launcher.mods"), x0, 16, Gold, 2);
        Label(c, Localizer.Get("launcher.modsHint"), x0, 38, Muted);
        if (Button(c, Localizer.Get("launcher.folder"), x0 + w - 150, 12, 150, 20)) OpenFolder(_modsDirectory);

        Bevel(c, x0, ModListTop - 6, w, BarTop - ModListTop, PanelDark, Panel, Ink);
        if (_mods.Count == 0)
        {
            foreach (var (line, i) in Wrap(Localizer.Get("launcher.noMods"), w - 32).Select((line, i) => (line, i)))
                Centered(c, line, x0 + w / 2, ModListTop + 60 + i * 11, Muted);
            return;
        }
        for (int row = 0; row < VisibleMods && _modScroll + row < _mods.Count; row++)
        {
            int index = _modScroll + row, y = ModListTop + row * ModRowHeight;
            var (id, enabled) = _mods[index];
            bool hover = Hover(x0 + 4, y - 2, w - 8, ModRowHeight - 2);
            if (hover) Fill(c, x0 + 4, y - 2, w - 8, ModRowHeight - 2, Panel);
            Bevel(c, x0 + 10, y, 12, 12, enabled ? Green : PanelDark, enabled ? GreenLight : Panel, enabled ? GreenDark : Ink);
            if (enabled)
            {
                c.Line(x0 + 13, y + 6, x0 + 15, y + 8, Ink);
                c.Line(x0 + 15, y + 8, x0 + 19, y + 3, Ink);
            }
            Label(c, id, x0 + 30, y + 3, enabled ? Text : Muted);
            if (Clicked(x0 + 4, y - 2, w - 8, ModRowHeight - 2)) ToggleMod(index);
        }
        if (_mods.Count > VisibleMods)
        {
            int track = BarTop - ModListTop - 16, thumb = Math.Max(10, track * VisibleMods / _mods.Count);
            int offset = (track - thumb) * _modScroll / Math.Max(1, _mods.Count - VisibleMods);
            Fill(c, x0 + w - 8, ModListTop - 2 + offset, 3, thumb, PanelLight);
        }
    }

    private void DrawSettings(PixelCanvas c)
    {
        int x0 = Sidebar + 16;
        Label(c, Localizer.Get("launcher.page.settings"), x0, 16, Gold, 2);

        Label(c, Localizer.Get("launcher.language"), x0, 52, Text);
        for (int i = 0; i < Localizer.Languages.Count; i++)
        {
            string code = Localizer.Languages[i];
            if (Button(c, Localizer.Get("language." + code), x0 + i * 108, 66, 100, 20, !Busy, Localizer.Language == code) &&
                Localizer.Language != code)
            {
                bool saved = Localizer.Select(code);
                SetStatus(Localizer.Get(saved ? _game is null ? "launcher.noGame" : "launcher.ready" : "language.saveError"));
            }
        }

        Label(c, Localizer.Get("launcher.updates"), x0, 104, Text);
        if (Button(c, Localizer.Get("launcher.update"), x0, 118, 208, 20, !Busy && !_options.Offline))
        {
            _nextCheck = _clock.Elapsed + UpdateInterval;
            Start(UpdateAsync());
        }
        Label(c, Localizer.Get("launcher.updatesHint"), x0, 144, Muted);

        Label(c, Localizer.Get("launcher.files"), x0, 172, Text);
        if (Button(c, Localizer.Get("launcher.dataFolder"), x0, 186, 208, 20)) OpenFolder(_root);

        int infoY = 224;
        Label(c, Localizer.Format("launcher.version", Updater.Version), x0, infoY, Muted);
        if (_installed is { } v)
        {
            Label(c, Localizer.Format("launcher.installed", v.Game), x0, infoY + 12, Muted);
            Label(c, Localizer.Format("launcher.assetsVersion", v.Assets), x0, infoY + 24, Muted);
            Label(c, Localizer.Format("launcher.release", v.Sequence), x0, infoY + 36, Muted);
        }
    }

    private void DrawBar(PixelCanvas c, float time)
    {
        int x0 = Sidebar, w = Screen.W - Sidebar;
        Fill(c, x0, BarTop, w, Screen.H - BarTop, PanelDark);
        Fill(c, x0, BarTop, w, 1, Ink);
        Fill(c, x0, BarTop + 1, w, 1, Panel);

        int playW = 132, playH = 34, playX = Screen.W - playW - 10, playY = BarTop + (Screen.H - BarTop - playH) / 2;
        int textW = playX - x0 - 24;
        var lines = Wrap(_status, textW);
        uint statusColor = _statusIsError ? Danger : Text;
        if (_updater.Transfer is { } transfer)
        {
            Label(c, lines[0], x0 + 12, BarTop + 10, statusColor);
            float progress = Math.Clamp(transfer.Received / (float)Math.Max(1, transfer.Expected), 0, 1);
            int barW = textW, fill = (int)((barW - 4) * progress);
            Bevel(c, x0 + 12, BarTop + 22, barW, 10, Ink, PanelDark, PanelDark);
            Fill(c, x0 + 14, BarTop + 24, fill, 6, Accent);
            Fill(c, x0 + 14, BarTop + 24, fill, 1, Text);
            // A light sweep across the filled part.
            int sweep = (int)(time * 90) % Math.Max(1, barW + 40) - 20;
            for (int dx = 0; dx < 6; dx++)
                if (sweep + dx >= 0 && sweep + dx < fill) Fill(c, x0 + 14 + sweep + dx, BarTop + 25, 1, 5, Mix(Accent, Text, 0.6f));
            Label(c, Localizer.Format("launcher.progress", Megabytes(transfer.Received), Megabytes(transfer.Expected)),
                x0 + 12, BarTop + 34, Muted);
        }
        else
        {
            for (int i = 0; i < Math.Min(3, lines.Count); i++)
                Label(c, lines[i], x0 + 12, BarTop + 12 + i * 10, statusColor);
        }

        bool enabled = CanPlay;
        bool hover = enabled && Hover(playX, playY, playW, playH);
        uint face = !enabled ? PanelLight : hover ? GreenLight : Green;
        Fill(c, playX - 1, playY - 1, playW + 2, playH + 2, enabled ? GoldDark : Ink);
        Bevel(c, playX, playY, playW, playH, face, enabled ? Rgb(0xE0, 0xFF, 0xB8) : PanelLight, enabled ? GreenDark : Panel);
        string label = _gameRunning ? Localizer.Get("launcher.inGame")
            : Busy ? Localizer.Get("launcher.wait")
            : Localizer.Get("launcher.play");
        int scale = Measure(label, 2) <= playW - 12 ? 2 : 1;
        Centered(c, label, playX + playW / 2, playY + (playH - 7 * scale) / 2, enabled ? Ink : Muted, scale);
        if (enabled && Clicked(playX, playY, playW, playH)) Start(LaunchAsync());
    }

    private static string Megabytes(long bytes) => Localizer.Number(bytes / 1048576f, "0.0");

    private static void SaveScreenshot(PixelFrame frame, string path)
    {
        const int scale = 2;
        var image = new RgbaImage(frame.W * scale, frame.H * scale);
        for (int y = 0; y < image.H; y++)
            for (int x = 0; x < image.W; x++)
                image.Px[y * image.W + x] = frame.Color[y / scale * frame.W + x / scale] | 0xFF000000u;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        image.Save(path);
    }

    public static string ErrorText(Exception error, string key = "launcher.error")
    {
        if (error is LocalizedException localized) return Localizer.Get(localized.Key);
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "launcher.log"), error + Environment.NewLine);
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
        return Localizer.Get(key);
    }
}
