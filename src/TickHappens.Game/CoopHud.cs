using System.Numerics;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;
using TickHappens.Localization;

namespace TickHappens.Game;

internal sealed class CoopHud(OnlineGame game, SettingsMenu settings)
{
    private static readonly uint[] TeamColors = [0xFFDEF08F, 0xFF99DFFF, 0xFFF9B478, 0xFFF3A9E4];
    private const int LabelHeight = 14, LabelSpacing = LabelHeight + 1;
    private const float DeathNoticeSeconds = 3.5f;
    private const uint Downed = 0xFF8989FF;
    private const uint Background = 0xFF151018, Foreground = 0xFFF4F1FF;
    private readonly PauseMenu _pauseMenu = new(settings, online: true);
    private readonly bool[] _wasAlive = new bool[CoopSession.MaxPlayers];
    private readonly string?[] _deathNotices = new string?[CoopSession.MaxPlayers];
    private readonly float[] _deathNoticeTime = new float[CoopSession.MaxPlayers];
    private RunWorld? _noticeWorld;

    public bool CapturesInput => _pauseMenu.IsOpen;

    public void Reset()
    {
        _pauseMenu.Reset();
        _noticeWorld = null;
    }

    public void Read(GameWindow window, PixelRenderer renderer)
    {
        if (game.World?.Phase == RunPhase.Dead) return;
        bool wasOpen = _pauseMenu.IsOpen;
        if (_pauseMenu.Read(window, renderer, canEnd: game.IsHost))
        {
            game.Leave();
            return;
        }
        if (game.IsHost && wasOpen != _pauseMenu.IsOpen) game.SetPaused(_pauseMenu.IsOpen);

    }

    public void DrawTeammates(PixelRenderer renderer, RunWorld world, Vector2[] positions)
    {
        if (game.Interrupted) return;

        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.UiW, h = renderer.UiH;
        Span<(int Left, int Right, int Top)> labels = stackalloc (int, int, int)[CoopSession.MaxPlayers];
        int count = 0;
        for (int slot = 0; slot < world.Players.Count; slot++)
        {
            var player = world.Players[slot];
            if (slot == world.Player.Id || !player.Active) continue;

            var position = positions[slot];
            var (x, y) = renderer.WorldToUi(position.X, position.Y);
            bool downed = player.Hp <= 0;
            if (x < 0 || x >= w || y < 0 || y >= h)
            {
                DrawArrow(ui, w, h, x, y, slot, downed);
                continue;
            }

            var (_, head) = renderer.WorldToUi(position.X, position.Y - (downed ? 4 : 28));
            string name = game.PlayerName(slot);
            int width = Math.Max(28, PixelFont.Measure(name) + 8);
            int left = Math.Clamp(x - width / 2, 2, Math.Max(2, w - width - 2));
            int top = PlaceLabel(labels[..count], left, width, head - LabelHeight, y + 4);
            labels[count++] = (left, left + width, top);

            uint color = downed ? Downed : TeamColors[slot];
            Fill(ui, left, top, width, LabelHeight, Background);
            PixelFont.DrawCentered(ui, name, left + width / 2, top + 1, color);
            Hud.Bar(ui, left + 3, top + 10, width - 6, 2,
                player.Hp / MathF.Max(1, RunWorld.MaxHealthFor(player)), color, 0xFF52404E);
        }
    }

    private static int PlaceLabel(ReadOnlySpan<(int Left, int Right, int Top)> labels,
        int left, int width, int above, int below)
    {
        int top = above;
        int direction = -1;
        for (int i = 0; i < labels.Length;)
        {
            if (direction < 0 && top <= Hud.RunHeaderBottom)
            {
                direction = 1;
                top = Math.Max(below, Hud.RunHeaderBottom + 1);
                i = 0;
                continue;
            }
            var other = labels[i++];
            if (left < other.Right && left + width > other.Left && Math.Abs(top - other.Top) < LabelSpacing)
            {
                top = other.Top + direction * LabelSpacing;
                i = 0;
            }
        }
        return top <= Hud.RunHeaderBottom ? Math.Max(below, Hud.RunHeaderBottom + 1) : top;
    }

    public void Draw(PixelRenderer renderer, RunWorld world, float frameDt, bool itemsOpen)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.UiW, h = renderer.UiH;
        if (!game.Interrupted)
        {
            DrawDeathNotices(ui, world, w, frameDt);
        }
        if (!game.Interrupted && !itemsOpen)
        {
            const int arrowGutter = 46;
            string ping = Localizer.Format("online.ping", game.IsHost ? Localizer.Get("online.hostPing")
                : game.LocalPing is int ms ? Localizer.Format("online.ms", ms) : "-");
            PixelFont.Draw(ui, ping, w - arrowGutter - PixelFont.Measure(ping), Hud.RunHeaderBottom + 5, Foreground, Background);
            for (int slot = 0; slot < world.Players.Count; slot++)
            {
                var player = world.Players[slot];
                if (slot == world.Player.Id || (!player.Active && game.PlayerHero(slot) is null)) continue;
                string name = $"{slot + 1}. {game.PlayerName(slot)}";
                string health = !player.Active ? Localizer.Get("online.disconnected") :
                    player.Hp <= 0 ? Localizer.Get("online.downed") : $"{(int)MathF.Ceiling(player.Hp)}/{(int)MathF.Ceiling(RunWorld.MaxHealthFor(player))}";
                if (player.Active && game.Ping(slot) is int peerPing)
                    health += "  " + Localizer.Format("online.ms", peerPing);
                int row = Hud.RunHeaderBottom + 17 + slot * 20;
                PixelFont.Draw(ui, name, w - arrowGutter - PixelFont.Measure(name), row, TeamColors[slot], Background);
                PixelFont.Draw(ui, health, w - arrowGutter - PixelFont.Measure(health), row + 8, Foreground, Background);
            }
            PixelFont.DrawCentered(ui, Localizer.Get("online.teamHint"), w / 2, h - 12, Foreground, Background);
        }
        if (world.Phase == RunPhase.Dead) return;
        if (_pauseMenu.IsOpen) _pauseMenu.Draw(renderer, canEnd: game.IsHost);
        else if (game.Paused)
            PixelFont.DrawCentered(ui, Localizer.Get("online.pausedByHost"), w / 2, h / 2 - 4, Foreground, Background);
    }

    private void DrawDeathNotices(PixelCanvas ui, RunWorld world, int width, float frameDt)
    {
        if (!ReferenceEquals(world, _noticeWorld))
        {
            _noticeWorld = world;
            Array.Clear(_wasAlive);
            Array.Clear(_deathNotices);
        }

        int y = Hud.RunHeaderBottom + 15;
        bool choosingLoot = world.Player.Hp > 0 && world.Player.ChestOfferFirst >= 0;
        for (int slot = 0; slot < world.Players.Count; slot++)
        {
            var player = world.Players[slot];
            if (_wasAlive[slot] && player.Active && player.Hp <= 0)
            {
                _deathNotices[slot] = Localizer.Format("online.playerDied", game.PlayerName(slot));
                _deathNoticeTime[slot] = DeathNoticeSeconds;
            }
            _wasAlive[slot] = player.Active && player.Hp > 0;

            if (choosingLoot || _deathNotices[slot] is not { } notice) continue;
            if (!game.Paused) _deathNoticeTime[slot] -= frameDt;
            if (_deathNoticeTime[slot] <= 0)
            {
                _deathNotices[slot] = null;
                continue;
            }

            int noticeWidth = PixelFont.Measure(notice) + 14;
            int x = (width - noticeWidth) / 2;
            Fill(ui, x, y, noticeWidth, 12, Background);
            Fill(ui, x, y, 2, 12, Downed);
            PixelFont.DrawCentered(ui, notice, width / 2, y + 2, Foreground);
            y += 14;
        }
    }

    private static void DrawArrow(PixelCanvas ui, int w, int h, int x, int y, int slot, bool downed)
    {
        var min = new Vector2(17, Hud.RunHeaderBottom + 16);
        var max = new Vector2(w - 18, h - 23);
        var center = (min + max) / 2;
        var delta = new Vector2(x, y) - center;
        var direction = Vector2.Normalize(delta);
        var tangent = new Vector2(-direction.Y, direction.X);
        var half = (max - min) / 2;
        float edge = MathF.Min(half.X / MathF.Max(1, MathF.Abs(delta.X)),
            half.Y / MathF.Max(1, MathF.Abs(delta.Y)));
        Vector2 tip = Vector2.Clamp(center + delta * edge + tangent * (slot - 1.5f) * 20, min, max);
        Vector2 tail = tip - direction * 14;

        uint color = downed ? Downed : TeamColors[slot];
        for (int i = -3; i <= 3; i++)
        {
            Vector2 start = tail + tangent * i;
            Vector2 end = tip + tangent * i;
            ui.Line(start.X, start.Y, end.X, end.Y, Background);
        }
        for (int i = -8; i <= 8; i++)
        {
            Vector2 start = tip - direction * 10 + tangent * i;
            ui.Line(start.X, start.Y, tip.X + direction.X, tip.Y + direction.Y, Background);
        }
        for (int i = -1; i <= 1; i++)
        {
            Vector2 start = tail + direction + tangent * i;
            ui.Line(start.X, start.Y, tip.X, tip.Y, color);
        }
        for (int i = -5; i <= 5; i++)
        {
            Vector2 start = tip - direction * 8 + tangent * i;
            ui.Line(start.X, start.Y, tip.X, tip.Y, color);
        }

        Vector2 badge = tip - direction * 22;
        int bx = (int)badge.X, by = (int)badge.Y;
        Fill(ui, bx - 4, by - 5, 9, 11, Background);
        ReadOnlySpan<char> number = "1234".AsSpan(slot, 1);
        PixelFont.Draw(ui, number, bx - 2, by - 3, color);
    }

    private static void Fill(PixelCanvas ui, int x, int y, int width, int height, uint color)
    {
        for (int yy = y; yy < y + height; yy++)
            ui.Line(x, yy, x + width - 1, yy, color);
    }
}
