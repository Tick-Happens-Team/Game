#if TICKHAPPENS_EOS
using TickHappens.Localization;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;

namespace TickHappens.Game.Online;

internal sealed class OnlineMenu(OnlineGame game)
{
    private enum Screen { Closed, Main, Code }
    private Screen _screen;
    private string _code = "";
    private const uint Teal = 0xFFDEF08F, Ink = 0xFF151018, Text = 0xFFF4F1FF, Panel = 0xFF332239;

    public bool IsOpen => _screen != Screen.Closed;

    public void Show() => _screen = Screen.Main;

    public void Read(GameWindow window, PixelRenderer renderer, string character, uint seed)
    {
        int w = renderer.ViewW, h = renderer.ViewH;
        var click = window.LeftClick;
        bool Click(int x, int y, int width, int height) =>
            click is { } point && point.X * w >= x && point.X * w < x + width &&
            point.Y * h >= y && point.Y * h < y + height;

        if (!IsOpen)
        {
            if (window.WasPressed(Key.F1) || Click(w / 2 - 90, h - 38, 180, 16)) Show();
            return;
        }
        if (window.WasPressed(Key.Escape) || Click(w / 2 - 90, h / 2 + 36, 180, 16))
        {
            if (game.Busy || game.InRoom) game.Leave();
            _screen = _screen == Screen.Code ? Screen.Main : Screen.Closed;
            return;
        }
        if (game.Busy) return;
        if (game.InRoom)
        {
            if (game.IsHost && (window.WasPressed(Key.Return) || Click(w / 2 - 90, h / 2 + 12, 180, 16)))
                game.Start();
            return;
        }
        if (_screen == Screen.Code)
        {
            char typed = window.PressedCodeCharacter();
            if (typed != '\0' && _code.Length < 6) _code += typed;
            if (window.WasPressed(Key.Backspace) && _code.Length != 0) _code = _code[..^1];
            if (_code.Length == 6 && (window.WasPressed(Key.Return) || Click(w / 2 - 90, h / 2 + 12, 180, 16)))
                game.Connect(_code, character, seed);
            return;
        }
        if (window.WasPressed(Key.F2) || Click(w / 2 - 90, h / 2 - 12, 180, 16)) game.Connect(null, character, seed);
        if (window.WasPressed(Key.F3) || Click(w / 2 - 90, h / 2 + 12, 180, 16))
        {
            _code = "";
            _screen = Screen.Code;
        }
    }

    public void Draw(PixelRenderer renderer)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int x = renderer.ViewW / 2, y = renderer.ViewH / 2;
        void Button(string label, int top, bool enabled = true)
        {
            for (int dy = 0; dy < 16; dy++)
                for (int dx = -90; dx < 90; dx++)
                    ui.Set(x + dx, top + dy, Panel);
            PixelFont.DrawCentered(ui, label, x, top + 4, enabled ? Teal : Text, Ink);
        }
        if (!IsOpen)
        {
            Button(Localizer.Get("online.entry"), renderer.ViewH - 38);
            return;
        }
        for (int dy = -60; dy < 58; dy++)
            for (int dx = -130; dx <= 130; dx++)
                ui.Set(x + dx, y + dy, Ink);
        PixelFont.DrawCentered(ui, Localizer.Get("online.title"), x, y - 53, Teal, Ink);
        string status = game.RoomCode is { } code ? Localizer.Format("online.room", code, game.PlayerCount) : game.Status;
        if (status.Length > 42) status = status[..42];
        PixelFont.DrawCentered(ui, status, x, y - 32, Text, Ink);
        if (!game.Busy)
        {
            if (game.InRoom)
            {
                if (game.IsHost) Button(Localizer.Get("online.start"), y + 12, game.PlayerCount >= 2);
            }
            else if (_screen == Screen.Code)
            {
                PixelFont.DrawCentered(ui, Localizer.Format("online.code", _code.PadRight(6, '_')), x, y - 9, Text, Ink);
                Button(Localizer.Get("online.connect"), y + 12, _code.Length == 6);
            }
            else
            {
                Button(Localizer.Get("online.host"), y - 12);
                Button(Localizer.Get("online.join"), y + 12);
            }
        }
        Button(Localizer.Get("online.back"), y + 36);
    }
}
#endif
