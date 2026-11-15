using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Localization;

namespace TickHappens.Game;

internal sealed class SettingsMenu(GameSettings settings, GameAudio audio)
{
    private const int Width = 244, EntryWidth = 112, RowHeight = 18, ButtonHeight = 16;
    private const uint Ink = 0xFF151018, Panel = 0xFF332239, Selected = 0xFF513B57;
    private const uint Text = 0xFFF4F1FF, Accent = 0xFFDEF08F;
    private static readonly string[] AudioLabels = ["settings.master", "settings.music", "settings.effects"];
    private static readonly string[] Actions = ["settings.up", "settings.down", "settings.left", "settings.right",
        "settings.dash", "settings.chest1", "settings.chest2", "settings.chest3"];
    private const int ControlsRow = 3, LanguageRow = 4, VsyncRow = 5;
    private bool _controls;
    private int _row;
    private int _binding = -1;
    private bool _vsync;
    private int RowCount => _controls ? Actions.Length + 2 : 7;
    private int Top(PixelRenderer renderer) => (renderer.UiH - RowCount * RowHeight) / 2;
    private static int EntryTop(PixelRenderer renderer) => renderer.UiH - 70;

    public void Open(GameWindow window)
    {
        _controls = false;
        _row = 0;
        _binding = -1;
        _vsync = window.Vsync;
    }

    public bool EntryPressed(GameWindow window, PixelRenderer renderer) =>
        window.WasPressed(Key.F3) || (window.LeftClick is { } point &&
            Math.Abs(point.X * window.Width / renderer.UiScale - renderer.UiW / 2) < EntryWidth / 2 &&
            point.Y * window.Height / renderer.UiScale - EntryTop(renderer) is >= 0 and < ButtonHeight);

    public void DrawEntry(PixelRenderer renderer)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int x = renderer.UiW / 2, y = EntryTop(renderer);
        Fill(ui, x - EntryWidth / 2, y, EntryWidth, ButtonHeight, Panel);
        PixelFont.DrawCentered(ui, Localizer.Get("settings.entry"), x, y + 4, Accent, Ink);
    }

    /// <summary>Returns true when the settings screen should close.</summary>
    public bool Read(GameWindow window, PixelRenderer renderer)
    {
        if (_binding >= 0)
        {
            if (window.WasPressed(Key.Escape)) _binding = -1;
            else
            {
                foreach (var key in GameSettings.BindableKeys)
                {
                    if (!window.WasPressed(key)) continue;
                    settings.Bind(_binding, key);
                    settings.Save();
                    _binding = -1;
                    break;
                }
            }
            return false;
        }
        if (window.WasPressed(Key.Escape))
        {
            if (!_controls) return true;
            ShowControls(false);
            return false;
        }

        if (window.WasPressed(Key.Up)) _row = (_row + RowCount - 1) % RowCount;
        if (window.WasPressed(Key.Down)) _row = (_row + 1) % RowCount;
        bool click = false;
        float relativeX = 0;
        if (window.LeftClick is { } point)
        {
            relativeX = point.X * window.Width / renderer.UiScale - renderer.UiW / 2;
            float relativeY = point.Y * window.Height / renderer.UiScale - Top(renderer);
            if (relativeX is >= -Width / 2 and < Width / 2 && relativeY >= 0 &&
                relativeY < RowCount * RowHeight && relativeY % RowHeight < ButtonHeight)
            {
                click = true;
                _row = (int)relativeY / RowHeight;
            }
        }
        bool activate = click || window.WasPressed(Key.Return);
        if (_controls)
        {
            if (!activate) return false;
            if (_row < Actions.Length) _binding = _row;
            else if (_row == Actions.Length)
            {
                settings.ResetKeys();
                settings.Save();
            }
            else ShowControls(false);
            return false;
        }

        if (_row < AudioLabels.Length)
        {
            int delta = (window.WasPressed(Key.Right) ? 5 : 0) - (window.WasPressed(Key.Left) ? 5 : 0);
            if (click && relativeX >= -4)
                SetVolume((int)MathF.Round((relativeX + 4) * 100 / 84));
            else if (delta != 0) SetVolume(Volume(_row) + delta);
        }
        if (window.WasPressed(Key.F4) || (activate && _row == LanguageRow))
        {
            string language = Localizer.Languages.First(code => code != Localizer.Language);
            if (!Localizer.Select(language)) Console.Error.WriteLine(Localizer.Get("language.saveError"));
        }
        else if (window.WasPressed(Key.F5) || (activate && _row == VsyncRow))
        {
            window.SetVsync(!window.Vsync);
            _vsync = window.Vsync;
        }
        else if (activate && _row == ControlsRow) ShowControls(true);
        else if (activate && _row == RowCount - 1) return true;
        return false;
    }

    private void ShowControls(bool controls)
    {
        _controls = controls;
        _row = controls ? 0 : ControlsRow;
    }

    private int Volume(int channel) => channel switch { 0 => settings.Master, 1 => settings.Music, _ => settings.Effects };

    private void SetVolume(int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (value == Volume(_row)) return;
        switch (_row)
        {
            case 0: settings.Master = value; break;
            case 1: settings.Music = value; break;
            case 2: settings.Effects = value; break;
        }
        audio.ApplyVolumes(settings);
        settings.Save();
    }

    public void Draw(PixelRenderer renderer)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int x = renderer.UiW / 2, y = Top(renderer);
        Fill(ui, x - Width / 2 - 6, y - 22, Width + 12, RowCount * RowHeight + 42, Ink);
        PixelFont.DrawCentered(ui, Localizer.Get(_controls ? "settings.controls" : "online.settingsTitle"),
            x, y - 15, Accent, Ink);
        for (int row = 0; row < RowCount; row++)
        {
            int top = y + row * RowHeight;
            uint color = row == _row ? Selected : Panel;
            Fill(ui, x - Width / 2, top, Width, ButtonHeight, color);
            if (_controls && row < Actions.Length)
            {
                PixelFont.Draw(ui, Localizer.Get(Actions[row]), x - Width / 2 + 6, top + 4, Text, Ink);
                string key = _binding == row ? Localizer.Get("settings.pressKey") : GameSettings.KeyLabel(settings.Keys[row]);
                PixelFont.Draw(ui, key, x + Width / 2 - 6 - PixelFont.Measure(key), top + 4, Accent, Ink);
            }
            else if (!_controls && row < AudioLabels.Length)
            {
                PixelFont.Draw(ui, Localizer.Get(AudioLabels[row]), x - Width / 2 + 6, top + 4, Text, Ink);
                int volume = Volume(row);
                Fill(ui, x - 4, top + 6, 84, 4, Ink);
                Fill(ui, x - 4, top + 6, 84 * volume / 100, 4, Accent);
                PixelFont.Draw(ui, $"{volume}%", x + 88, top + 4, Text, Ink);
            }
            else
            {
                string label = _controls ? (row == Actions.Length ? "settings.reset" : "online.pauseBack") : row switch
                {
                    ControlsRow => "settings.controls",
                    LanguageRow => "online.language",
                    VsyncRow => _vsync ? "online.vsyncOn" : "online.vsyncOff",
                    _ => "online.pauseBack",
                };
                PixelFont.DrawCentered(ui, Localizer.Get(label), x, top + 4, Text, Ink);
            }
        }
        PixelFont.DrawCentered(ui, Localizer.Get(_binding >= 0 ? "settings.cancelBind" :
            _controls ? "settings.bindHelp" : "settings.help"), x, y + RowCount * RowHeight + 5, Accent, Ink);
    }

    private static void Fill(PixelCanvas ui, int x, int y, int width, int height, uint color)
    {
        for (int row = y; row < y + height; row++)
            for (int col = x; col < x + width; col++) ui.Set(col, row, color);
    }
}
