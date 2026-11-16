using System.Diagnostics;
using System.Reflection;
using SDL3;
using TickHappens.Engine.Platform;
using TickHappens.Launcher;
using TickHappens.Localization;

internal static class Program
{
    private static int Main(string[] args)
    {
        Localizer.Initialize();
        var options = ParseOptions(args);
        string root = Environment.GetEnvironmentVariable("TICKHAPPENS_LAUNCHER_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens");
        Directory.CreateDirectory(root);
        const string defaultUrl = "https://github.com/Tick-Happens-Team/Game/releases/latest/download/channel.json";
        var url = new Uri(Environment.GetEnvironmentVariable("TICKHAPPENS_CHANNEL_URL") ?? defaultUrl);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReleasePublicKey.pem");
        using var reader = resource is null ? null : new StreamReader(resource);
        var updater = new Updater(root, url, reader?.ReadToEnd() ?? "");
        try
        {
            using var lease = updater.Lock();
            string? newer = null;
            try { newer = options.Screenshot is null ? updater.CurrentEntry("launcher") : null; }
            catch (Exception error) when (error is IOException or InvalidDataException) { }
            if (newer is not null)
            {
                lease.Dispose();
                Process.Start(new ProcessStartInfo(newer) { WorkingDirectory = Path.GetDirectoryName(newer)! });
                return 0;
            }
            using (var window = new GameWindow(Localizer.Get("launcher.title"), 1280, 720, vsync: true))
            using (var screen = new Screen(window.Gl))
            {
                var app = new LauncherApp(updater, root, options);
                app.Run(window, screen);
                newer = app.NewLauncher;
            }
            if (newer is not null)
            {
                lease.Dispose();
                Process.Start(new ProcessStartInfo(newer) { WorkingDirectory = Path.GetDirectoryName(newer)! });
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            // The window may not exist (no OpenGL 4.3, or another launcher holds the lock), so use SDL's native box.
            SDL.ShowSimpleMessageBox(SDL.MessageBoxFlags.Error, Localizer.Get("launcher.title"),
                LauncherApp.ErrorText(error, error is IOException ? "launcher.alreadyRunning" : "launcher.error"), 0);
            return 1;
        }
    }

    /// <summary>Development options: --offline skips update checks; --page, --frames and --screenshot capture a review image after N frames.</summary>
    private static LauncherOptions ParseOptions(string[] args)
    {
        bool offline = false;
        var page = Page.Play;
        int frames = 1;
        string? screenshot = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--offline": offline = true; break;
                case "--page" when i + 1 < args.Length: page = Enum.Parse<Page>(args[++i], ignoreCase: true); break;
                case "--frames" when i + 1 < args.Length: frames = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--screenshot" when i + 1 < args.Length: screenshot = args[++i]; break;
                default: throw new ArgumentException("Unknown launcher argument: " + args[i]);
            }
        }
        return new(offline, page, frames, screenshot);
    }
}
