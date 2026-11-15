using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Localization;

namespace TickHappens.Game;

internal sealed class PauseMenu(SettingsMenu settings, bool online = false)
{
    private enum Page { Closed, Pause, Settings, End }
    private const int ButtonWidth = 196, ButtonHeight = 16;
    private const int FirstRow = -18, SecondRow = 2, ThirdRow = 22;
    private const uint Background = 0xFF151018, Panel = 0xFF332239, Foreground = 0xFFF4F1FF;

    private Page _page;
    public bool IsOpen => _page != Page.Closed;

    public void Reset() => _page = Page.Closed;

    /// <summary>Returns true when the player selects the action to leave the run.</summary>
    public bool Read(GameWindow window, PixelRenderer renderer, bool canEnd = true)
    {
        if (_page == Page.Settings)
        {
            if (settings.Read(window, renderer)) _page = Page.Pause;
            return false;
        }
        if (window.WasPressed(Key.Escape) || (!online && window.WasPressed(Key.P)))
        {
            _page = _page switch
            {
                Page.End => Page.Pause,
                Page.Pause => Page.Closed,
                _ => Page.Pause,
            };
            return false;
        }

        switch (_page)
        {
            case Page.Pause:
                if (window.WasPressed(Key.Return) || Click(FirstRow))
                    Reset();
                else if (window.WasPressed(Key.F4) || Click(SecondRow))
                {
                    settings.Open(window);
                    _page = Page.Settings;
                }
                else if (canEnd && (window.WasPressed(online ? Key.F2 : Key.R) || Click(ThirdRow)))
                {
                    if (!online) return true;
                    _page = Page.End;
                }
                break;
            case Page.End:
                if (canEnd && (window.WasPressed(Key.Return) || Click(SecondRow))) return true;
                if (Click(ThirdRow)) _page = Page.Pause;
                break;
        }
        return false;

        bool Click(int row) => window.LeftClick is { } point &&
            Math.Abs(point.X * renderer.UiW - renderer.UiW / 2) < ButtonWidth / 2 &&
            point.Y * renderer.UiH - renderer.UiH / 2 >= row &&
            point.Y * renderer.UiH - renderer.UiH / 2 < row + ButtonHeight;
    }

    public void Draw(PixelRenderer renderer, bool canEnd = true)
    {
        if (!IsOpen) return;
        if (_page == Page.Settings)
        {
            settings.Draw(renderer);
            return;
        }
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int x = renderer.UiW / 2, y = renderer.UiH / 2;
        Fill(x - 103, y - 44, 206, 88, Background);
        string heading = _page switch
        {
            Page.End => "online.confirmEnd",
            _ => online ? "online.pauseMenu" : "hud.paused",
        };
        PixelFont.DrawCentered(ui, Localizer.Get(heading), x, y - 36, Foreground, Background);
        switch (_page)
        {
            case Page.Pause:
                Button("online.resume", FirstRow);
                Button("online.settings", SecondRow);
                if (canEnd) Button(online ? "online.end" : "hud.heroSelect", ThirdRow);
                break;
            case Page.End:
                Button("online.endConfirmButton", SecondRow);
                Button("online.pauseBack", ThirdRow);
                break;
        }

        void Button(string key, int row)
        {
            Fill(x - ButtonWidth / 2, y + row, ButtonWidth, ButtonHeight, Panel);
            PixelFont.DrawCentered(ui, Localizer.Get(key), x, y + row + 4, Foreground, Background);
        }

        void Fill(int left, int top, int width, int height, uint color)
        {
            for (int row = top; row < top + height; row++)
                for (int col = left; col < left + width; col++) ui.Set(col, row, color);
        }
    }
}
