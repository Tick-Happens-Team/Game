using System.Diagnostics;
using System.Reflection;
using TickHappens.Launcher;
using TickHappens.Localization;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Localizer.Initialize();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens");
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
            try { newer = updater.CurrentEntry("launcher"); }
            catch (Exception error) when (error is IOException or InvalidDataException) { }
            if (newer is not null)
            {
                lease.Dispose();
                Process.Start(new ProcessStartInfo(newer) { WorkingDirectory = Path.GetDirectoryName(newer)! });
                return;
            }
            using var window = new LauncherForm(updater, root);
            Application.Run(window);
            newer = window.NewLauncher;
            if (newer is not null)
            {
                lease.Dispose();
                Process.Start(new ProcessStartInfo(newer) { WorkingDirectory = Path.GetDirectoryName(newer)! });
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(LauncherForm.ErrorText(error), Localizer.Get("launcher.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
