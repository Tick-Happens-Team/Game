using System.Diagnostics;
using System.Text.Json;

namespace TickHappens.Launcher;

internal sealed class LauncherForm : Form
{
    private readonly Updater _updater;
    private readonly string _modsDirectory;
    private readonly string _profile;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly CheckedListBox _mods = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly Button _play = new() { Text = "Play", Enabled = false, AutoSize = true };
    private readonly Button _update = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button _folder = new() { Text = "Open mods folder", AutoSize = true };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 15 * 60 * 1000 };
    private bool _busy, _closeRequested;
    private string? _game;

    public string? NewLauncher { get; private set; }

    public LauncherForm(Updater updater, string root)
    {
        _updater = updater;
        _modsDirectory = Path.Combine(root, "mods");
        _profile = Path.Combine(root, "enabled-mods.json");
        Directory.CreateDirectory(_modsDirectory);
        Text = "TickHappens Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(500, 320);
        ClientSize = new Size(640, 360);
        Font = new Font("Segoe UI", 10);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([_play, _update, _folder]);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_status);
        layout.Controls.Add(new Label { Text = "Local item mods", AutoSize = true });
        layout.Controls.Add(_mods);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        Shown += async (_, _) => await RefreshAsync();
        _update.Click += async (_, _) => await RefreshAsync();
        _play.Click += async (_, _) => await LaunchAsync();
        _folder.Click += (_, _) => Process.Start(new ProcessStartInfo(_modsDirectory) { UseShellExecute = true });
        _mods.ItemCheck += (_, e) => SaveSelection(e);
        FormClosing += (_, e) =>
        {
            if (!_busy) return;
            e.Cancel = true;
            _closeRequested = true;
            _cancellation.Cancel();
        };
        _updateTimer.Tick += async (_, _) => { if (!_busy) await RefreshAsync(); };
        _updateTimer.Start();
        FormClosed += (_, _) =>
        {
            _updateTimer.Dispose();
            _cancellation.Dispose();
        };
        Activated += (_, _) => { if (!_busy) ReloadMods(); };
        ReloadMods();
    }

    private bool _loadingMods;

    private void ReloadMods()
    {
        _loadingMods = true;
        try
        {
            var selected = File.Exists(_profile)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(_profile)) ?? []
                : [];
            _mods.Items.Clear();
            foreach (string dir in Directory.EnumerateDirectories(_modsDirectory).Order(StringComparer.Ordinal))
                if (File.Exists(Path.Combine(dir, "items.json")))
                    _mods.Items.Add(Path.GetFileName(dir), selected.Contains(Path.GetFileName(dir), StringComparer.Ordinal));
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _loadingMods = false; }
    }

    private void SaveSelection(ItemCheckEventArgs change)
    {
        if (_loadingMods) return;
        var selection = new List<string>();
        for (int i = 0; i < _mods.Items.Count; i++)
            if (i == change.Index ? change.NewValue == CheckState.Checked : _mods.GetItemChecked(i))
                selection.Add((string)_mods.Items[i]);
        try
        {
            File.WriteAllText(_profile + ".new", JsonSerializer.Serialize(selection));
            File.Move(_profile + ".new", _profile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _status.Text = error.Message;
            change.NewValue = change.CurrentValue;
        }
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        try
        {
            NewLauncher = await _updater.UpdateAsync(message => _status.Text = message, _cancellation.Token);
            if (NewLauncher is null) _status.Text = "Game is up to date.";
        }
        catch (Exception error) { _status.Text = error.Message; }
        if (NewLauncher is null && !_closeRequested)
        {
            try { _game = await Task.Run(() => _updater.CurrentEntry("game")); }
            catch (Exception error) { _game = null; _status.Text = error.Message; }
        }
        SetBusy(false);
        if (NewLauncher is not null || _closeRequested) Close();
    }

    private async Task LaunchAsync()
    {
        SetBusy(true);
        try
        {
            string entry = await Task.Run(() => _updater.CurrentEntry("game")) ?? throw new IOException("Install the game first.");
            var start = new ProcessStartInfo(entry) { WorkingDirectory = Path.GetDirectoryName(entry)! };
            start.Environment["TICKHAPPENS_MODS_ROOT"] = _modsDirectory;
            start.Environment["TICKHAPPENS_MOD_PROFILE"] = _profile;
            using var game = Process.Start(start) ?? throw new IOException("Could not start the game.");
            _status.Text = "Game is running.";
            // Keep the installation lease until this process exits.
            await game.WaitForExitAsync();
            _status.Text = game.ExitCode == 0 ? "Ready to play." : $"Game exited with code {game.ExitCode}.";
        }
        catch (Exception error) { _status.Text = error.Message; }
        SetBusy(false);
        if (_closeRequested) Close();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _play.Enabled = !busy && _game is not null;
        _update.Enabled = _mods.Enabled = !busy;
    }
}
