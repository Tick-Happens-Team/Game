using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Localization;

namespace TickHappens.Game.Online;

internal sealed partial class OnlineMenu
{
    private const uint Stage = 0xFF291D2B, Edge = 0xFF695269, Muted = 0xFFC5ACC7;
    private const uint StageLight = 0xFFD3A18C, Highlight = 0xFF4D354D;
    private const int PortraitStep = 46, PortraitHeight = 30, ButtonHeight = 18;

    private readonly record struct RoomLayout(int X, int Y, int Width, int Height)
    {
        public int Center => X + Width / 2;
        public int Foot => Y + Height - 126;
        public int Roster => Y + Height - 69;
        public int Buttons => Y + Height - 26;
        public int ButtonWidth => Math.Min(164, (Width - 32) / 3);
        public int ButtonX(int index) => Center - ButtonWidth * 3 / 2 - 6 + index * (ButtonWidth + 6);
        public int Portraits => Math.Max(1, (Width - 68) / PortraitStep);
    }

    private static RoomLayout Layout(PixelRenderer renderer)
    {
        int width = Math.Min(560, renderer.UiW - 12), height = Math.Min(300, renderer.UiH - 12);
        return new RoomLayout((renderer.UiW - width) / 2, (renderer.UiH - height) / 2, width, height);
    }

    private (int First, int Count, int Left) RosterLayout(RoomLayout layout)
    {
        int first = game.SelectedHero / layout.Portraits * layout.Portraits;
        int count = Math.Min(layout.Portraits, game.HeroCount - first);
        return (first, count, layout.Center - count * PortraitStep / 2);
    }

    private static bool Hits(PixelRenderer renderer, (float X, float Y)? point, int x, int y, int width, int height)
    {
        if (point is not { } p) return false;
        float px = p.X * renderer.UiW, py = p.Y * renderer.UiH;
        return px >= x && px < x + width && py >= y && py < y + height;
    }

    private void ReadRoom(GameWindow window, PixelRenderer renderer)
    {
        var layout = Layout(renderer);
        var click = window.LeftClick;
        if (Hits(renderer, click, layout.ButtonX(0), layout.Buttons, layout.ButtonWidth, ButtonHeight))
        {
            Back();
            return;
        }
        if (game.LocalSlot is not null)
        {
            if (window.WasPressed(Key.F4) ||
                Hits(renderer, click, layout.ButtonX(1), layout.Buttons, layout.ButtonWidth, ButtonHeight))
            {
                _editingNickname = game.Nickname;
                window.SetTextInput(true);
                return;
            }

            var (first, count, left) = RosterLayout(layout);
            int step = (window.WasPressed(Key.Right) || window.WasPressed(Key.D) ||
                Hits(renderer, click, left + count * PortraitStep + 3, layout.Roster, 24, PortraitHeight) ? 1 : 0)
                - (window.WasPressed(Key.Left) || window.WasPressed(Key.A) ||
                Hits(renderer, click, left - 27, layout.Roster, 24, PortraitHeight) ? 1 : 0);
            int hero = (game.SelectedHero + step + game.HeroCount) % game.HeroCount;
            for (int i = 0; i < count; i++)
                if (Hits(renderer, click, left + i * PortraitStep, layout.Roster, PortraitStep - 4, PortraitHeight))
                {
                    hero = first + i;
                    break;
                }
            if (hero != game.SelectedHero) game.EditProfile(hero, game.Nickname);
        }

        if (game.CanStart && (window.WasPressed(Key.Return) ||
            Hits(renderer, click, layout.ButtonX(2), layout.Buttons, layout.ButtonWidth, ButtonHeight)))
            game.Start();
    }

    private void DrawRoom(PixelRenderer renderer, PixelCanvas ui, string title, string status, float time,
        (float X, float Y)? mouse)
    {
        var layout = Layout(renderer);
        Hud.Frame(ui, layout.X, layout.Y, layout.Width, layout.Height, Edge, Ink);
        PixelFont.DrawCentered(ui, title, layout.Center, layout.Y + 10, Teal, Ink, 2);
        DrawFitted(ui, status, layout.Center, layout.Y + 30, layout.Width - 20, Muted);

        int stageTop = layout.Y + 45;
        int stageWidth = layout.Width - 16;
        Hud.Frame(ui, layout.X + 8, stageTop, stageWidth, layout.Foot + 38 - stageTop, Edge, Stage);
        int bayWidth = stageWidth / CoopSession.MaxPlayers;
        int heroScale = Math.Clamp(Math.Min((layout.Foot - stageTop - 7) / 25, (bayWidth - 8) / 28), 1, 4);
        for (int slot = 0; slot < CoopSession.MaxPlayers; slot++)
        {
            int x = layout.X + 8 + bayWidth * slot + bayWidth / 2;
            bool present = game.PlayerPresent(slot);
            bool local = game.LocalSlot == slot;
            uint accent = local ? Teal : present ? StageLight : Edge;
            int? hero = local ? game.SelectedHero : game.PlayerHero(slot);
            if (present)
                for (int radius = Math.Min(bayWidth / 2 - 5, 45); radius > 6; radius -= 7)
                    ui.AlphaDisc(x, layout.Foot - 12, radius, accent, 0.045f);

            Hud.Pedestal(ui, x, layout.Foot, Math.Min(40, bayWidth / 2 - 6), 9, accent);
            if (present && hero is int index)
                preview.DrawHero(ui, index, time + slot * 0.19f, x, layout.Foot + 1, heroScale);

            DrawFitted(ui, present ? game.PlayerName(slot) : Localizer.Get("online.emptySlot"),
                x, layout.Foot + 13, bayWidth - 6, present ? Text : Muted);
            if (hero is int selected && present)
                DrawFitted(ui, game.HeroName(selected), x, layout.Foot + 23, bayWidth - 6, accent);
            if (present && !(game.IsHost && slot == 0) && game.Ping(local ? 0 : slot) is int ms)
                DrawFitted(ui, Localizer.Format("online.ms", ms), x, layout.Foot + 31, bayWidth - 6, Muted);
        }

        void Button(string label, int x, int y, int width, int height, bool enabled = true)
        {
            bool hover = enabled && Hits(renderer, mouse, x, y, width, height);
            Hud.Frame(ui, x, y, width, height, enabled ? Teal : Edge, hover ? Highlight : Panel);
            DrawFitted(ui, label, x + width / 2, y + (height - 7) / 2, width - 6, enabled ? Text : Muted);
        }

        if (game.LocalSlot is not null)
        {
            var (first, count, left) = RosterLayout(layout);
            Button("<", left - 27, layout.Roster, 24, PortraitHeight);
            Button(">", left + count * PortraitStep + 3, layout.Roster, 24, PortraitHeight);
            int focus = game.SelectedHero;
            for (int i = 0; i < count; i++)
            {
                int index = first + i, x = left + i * PortraitStep;
                bool selected = index == game.SelectedHero;
                bool hover = Hits(renderer, mouse, x, layout.Roster, PortraitStep - 4, PortraitHeight);
                if (hover) focus = index;
                Hud.Frame(ui, x, layout.Roster, PortraitStep - 4, PortraitHeight,
                    selected ? Teal : hover ? StageLight : Edge, selected || hover ? Highlight : Panel);
                preview.DrawHero(ui, index, selected ? time : 0, x + (PortraitStep - 4) / 2, layout.Roster + 25, 1);
            }
            DrawFitted(ui, game.HeroName(focus), layout.Center, layout.Roster + 33, layout.Width - 20, Teal);
            Button(Localizer.Get("online.editNickname"), layout.ButtonX(1), layout.Buttons, layout.ButtonWidth, ButtonHeight);
        }

        Button(Localizer.Get("online.back"), layout.ButtonX(0), layout.Buttons, layout.ButtonWidth, ButtonHeight);
        if (game.IsHost)
            Button(Localizer.Get("online.start"), layout.ButtonX(2), layout.Buttons, layout.ButtonWidth, ButtonHeight, game.CanStart);
        else
            DrawFitted(ui, Localizer.Get("online.waitingHost"), layout.ButtonX(2) + layout.ButtonWidth / 2,
                layout.Buttons + 5, layout.ButtonWidth, Muted);
    }

    private static void DrawFitted(PixelCanvas ui, string text, int x, int y, int width, uint color)
    {
        int count = Math.Clamp(width / PixelFont.Advance, 0, text.Length);
        var label = text.AsSpan(0, count);
        PixelFont.Draw(ui, label, x - PixelFont.Measure(label) / 2, y, color, Ink);
    }
}
