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
    private string? _editingNickname;
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

    public void EndNicknameInput(GameWindow window)
    {
        if (_editingNickname is null) return;
        _editingNickname = null;
        window.SetTextInput(false);
    }

    public void Read(GameWindow window, PixelRenderer renderer, string character, uint seed)
    {
        int w = renderer.UiW, h = renderer.UiH;
        var click = window.LeftClick;
        bool Click(int x, int y, int width, int height) =>
            click is { } point && point.X * window.Width / renderer.UiScale >= x &&
            point.X * window.Width / renderer.UiScale < x + width &&
            point.Y * window.Height / renderer.UiScale >= y &&
            point.Y * window.Height / renderer.UiScale < y + height;

        if (_editingNickname is not null)
        {
            bool confirm = window.WasPressed(Key.Return) || Click(w / 2 - 90, h / 2 + 8, 180, 16);
            if (confirm || window.WasPressed(Key.Escape) || game.LocalSlot is null)
            {
                if (confirm) game.EditProfile(game.SelectedHero, _editingNickname);
                _editingNickname = null;
                window.SetTextInput(false);
            }
            else
            {
                foreach (char c in window.TextInput)
                    if (_editingNickname.Length < CoopSession.NicknameLimit && CoopSession.NicknameCharacter(c))
                        _editingNickname += c;
                if (window.WasPressed(Key.Backspace) && _editingNickname.Length != 0)
                    _editingNickname = _editingNickname[..^1];
            }
            return;
        }

        if (!IsOpen)
        {
            if (window.WasPressed(Key.F1) || Click(w / 2 - 90, h - 38, 180, 16)) Show();
            return;
        }
        if (window.WasPressed(Key.Escape) || Click(w / 2 - 90, h / 2 + (game.InRoom ? 58 : 36), 180, 16))
        {
            if (game.Busy || game.InRoom) game.Leave();
            _screen = _screen == Screen.Code ? Screen.Main : Screen.Closed;
            return;
        }
        if (game.Busy) return;
        if (game.InRoom)
        {
            if (game.LocalSlot is not null)
            {
                int step = (window.WasPressed(Key.Right) || window.WasPressed(Key.D) ||
                    Click(w / 2 + 66, h / 2 + 14, 24, 16) ? 1 : 0)
                    - (window.WasPressed(Key.Left) || window.WasPressed(Key.A) ||
                    Click(w / 2 - 90, h / 2 + 14, 24, 16) ? 1 : 0);
                if (step != 0)
                    game.EditProfile((game.SelectedHero + step + game.HeroCount) % game.HeroCount, game.Nickname);
                if (window.WasPressed(Key.F4) || Click(w / 2 - 90, h / 2 - 4, 180, 16))
                {
                    _editingNickname = game.Nickname;
                    window.SetTextInput(true);
                }
            }
            if (game.IsHost && (window.WasPressed(Key.Return) || Click(w / 2 - 90, h / 2 + 36, 180, 16)))
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
        int x = renderer.UiW / 2, y = renderer.UiH / 2;
        void Button(string label, int top, bool enabled = true, int offset = 0, int width = 180)
        {
            int center = x + offset;
            for (int dy = 0; dy < 16; dy++)
                for (int dx = -width / 2; dx < width / 2; dx++)
                    ui.Set(center + dx, top + dy, Panel);
            PixelFont.DrawCentered(ui, label, center, top + 4, enabled ? Teal : Text, Ink);
        }
        if (!IsOpen)
        {
            Button(Localizer.Get("online.entry"), renderer.UiH - 38);
            return;
        }
        int halfWidth = Math.Min(renderer.UiW / 2 - 3, game.InRoom ? 185 : 130);
        for (int dy = -82; dy < 80; dy++)
            for (int dx = -halfWidth; dx <= halfWidth; dx++)
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
                if (_editingNickname is not null)
                {
                    PixelFont.DrawCentered(ui, Localizer.Get("online.nickname"), x, y - 28, Teal, Ink);
                    PixelFont.DrawCentered(ui, _editingNickname + "_", x, y - 13, Text, Ink);
                    Button(Localizer.Get("online.confirmNickname"), y + 8);
                    return;
                }
                int row = y - 38;
                for (int slot = 0; slot < CoopSession.MaxPlayers; slot++)
                {
                    if (!game.PlayerPresent(slot)) continue;
                    string hero = game.PlayerHero(slot) is int index ? game.HeroName(index) : "-";
                    string ping = game.Ping(slot == game.LocalSlot ? 0 : slot) is int ms
                        ? Localizer.Format("online.ms", ms) : "-";
                    if (game.IsHost && slot == 0) ping = "-";
                    string line = $"{game.PlayerName(slot)}  |  {hero}  |  {ping}";
                    int max = (halfWidth * 2 - 10) / 6;
                    PixelFont.DrawCentered(ui, line[..Math.Min(max, line.Length)], x, row,
                        game.LocalSlot == slot ? Teal : Text, Ink);
                    row += 8;
                }
                if (game.LocalSlot is not null)
                {
                    Button(Localizer.Get("online.editNickname"), y - 4);
                    Button("<", y + 14, offset: -78, width: 24);
                    Button(">", y + 14, offset: 78, width: 24);
                    PixelFont.DrawCentered(ui, game.HeroName(game.SelectedHero), x, y + 18, Teal, Ink);
                }
                if (game.IsHost) Button(Localizer.Get("online.start"), y + 36, game.CanStart);
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
        Button(Localizer.Get("online.back"), y + (game.InRoom ? 58 : 36));
    }
}
