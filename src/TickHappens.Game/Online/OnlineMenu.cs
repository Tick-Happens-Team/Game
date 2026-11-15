using System.Net;
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
#if TICKHAPPENS_EOS
    private bool _eos;
#else
    private const bool EosAvailable = false;
#endif
    private bool IsEos =>
#if TICKHAPPENS_EOS
        _eos;
#else
        EosAvailable;
#endif

    private IPEndPoint? ParseAddress()
    {
        // Reject shorthand IPv4 forms such as "127.1".
        string host = _code.Split(':', 2)[0];
        return host.Count(c => c == '.') == 3 && IPEndPoint.TryParse(_code, out var endpoint) &&
            (!_code.Contains(':') || endpoint.Port != 0)
            ? new IPEndPoint(endpoint.Address, endpoint.Port == 0 ? LanTransport.DefaultPort : endpoint.Port)
            : null;
    }
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
            if (!IsEos)
            {
                if (window.WasPressed(Key.Period)) typed = '.';
                if (window.WasPressed(Key.Semicolon)) typed = ':';
                if (typed is not (>= '0' and <= '9' or '.' or ':')) typed = '\0';
            }
            if (typed != '\0' && _code.Length < (IsEos ? 6 : 21)) _code += typed;
            if (window.WasPressed(Key.Backspace) && _code.Length != 0) _code = _code[..^1];
            if (window.WasPressed(Key.Return) || Click(w / 2 - 90, h / 2 + 12, 180, 16))
            {
#if TICKHAPPENS_EOS
                if (IsEos && _code.Length == 6) game.Connect(_code, character, seed);
#endif
                if (!IsEos && ParseAddress() is { } address) game.ConnectLan(false, address, character, seed);
            }
            return;
        }
#if TICKHAPPENS_EOS
        if (window.WasPressed(Key.F6) || Click(w / 2 - 90, h / 2 + 59, 180, 16))
        {
            _eos = !_eos;
            return;
        }
        if (IsEos && (window.WasPressed(Key.F5) || Click(w / 2 - 90, h / 2 + 12, 180, 16)))
        {
            game.ForceRelays = !game.ForceRelays;
            return;
        }
#endif
        if (window.WasPressed(Key.F2) || Click(w / 2 - 90, h / 2 - 32, 180, 16))
        {
#if TICKHAPPENS_EOS
            if (IsEos) game.Connect(null, character, seed);
            else
#endif
                game.ConnectLan(true, null, character, seed);
        }
        else if (window.WasPressed(Key.F3) || Click(w / 2 - 90, h / 2 - 10, 180, 16))
        {
            if (IsEos) OpenAddress();
            else game.ConnectLan(false, null, character, seed);
        }
        else if (!IsEos && (window.WasPressed(Key.F5) || Click(w / 2 - 90, h / 2 + 12, 180, 16)))
            OpenAddress();
    }

    private void OpenAddress()
    {
        _code = "";
        _screen = Screen.Code;
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
        for (int dy = -82; dy < 80; dy++)
            for (int dx = -130; dx <= 130; dx++)
                ui.Set(x + dx, y + dy, Ink);
        PixelFont.DrawCentered(ui, Localizer.Get(IsEos ? "online.title" : "online.lanTitle"), x, y - 73, Teal, Ink);
        string status = game.RoomCode is { } code ? Localizer.Format("online.room", code, game.PlayerCount)
            : game.LanPort is int port ? Localizer.Format("online.lanRoom", port, game.PlayerCount)
            : game.Status;
        if (status.Length > 42) status = status[..42];
        PixelFont.DrawCentered(ui, status, x, y - 52, Text, Ink);
        if (!game.Busy)
        {
            if (game.InRoom)
            {
                if (game.IsHost) Button(Localizer.Get("online.start"), y + 12, game.PlayerCount >= 2);
            }
            else if (_screen == Screen.Code)
            {
                PixelFont.DrawCentered(ui, IsEos ? Localizer.Format("online.code", _code.PadRight(6, '_'))
                    : Localizer.Format("online.lanAddress", _code + "_"), x, y - 9, Text, Ink);
                Button(Localizer.Get("online.connect"), y + 12, IsEos ? _code.Length == 6 : ParseAddress() is not null);
            }
            else
            {
                Button(Localizer.Get("online.host"), y - 32);
                Button(Localizer.Get(IsEos ? "online.join" : "online.lanDiscover"), y - 10);
                if (!IsEos) Button(Localizer.Get("online.lanDirect"), y + 12);
#if TICKHAPPENS_EOS
                else Button(Localizer.Get(game.ForceRelays ? "online.relayOn" : "online.relayOff"), y + 12);
                Button(Localizer.Get(IsEos ? "online.switchLan" : "online.switchEos"), y + 59);
#endif
            }
        }
        Button(Localizer.Get("online.back"), y + 36);
    }
}
