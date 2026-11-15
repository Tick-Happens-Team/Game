using System.Numerics;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Online;
using TickHappens.Game.Sim;
using TickHappens.Localization;

namespace TickHappens.Game;

internal sealed class CoopHud(OnlineGame game)
{
    private static readonly uint[] TeamColors = [0xFFDEF08F, 0xFF99DFFF, 0xFFF9B478, 0xFFF3A9E4];
    private const uint Background = 0xFF151018, Foreground = 0xFFF4F1FF;
    private readonly PauseMenu _pauseMenu = new(online: true);
    private bool _tab;
    private int _itemOffset;
    public bool CapturesInput => _pauseMenu.IsOpen || _tab;

    public void Reset()
    {
        _pauseMenu.Reset();
        _tab = false;
        _itemOffset = 0;
    }

    public void Read(GameWindow window, PixelRenderer renderer)
    {
        bool wasOpen = _pauseMenu.IsOpen;
        if (_pauseMenu.Read(window, renderer, canEnd: game.IsHost))
        {
            game.Leave();
            return;
        }
        if (game.IsHost && wasOpen != _pauseMenu.IsOpen) game.SetPaused(_pauseMenu.IsOpen);

        _tab = !_pauseMenu.IsOpen && !game.Interrupted && window.IsDown(Key.Tab);
        if (_tab)
        {
            if (window.WasPressed(Key.Down)) _itemOffset++;
            if (window.WasPressed(Key.Up)) _itemOffset = Math.Max(0, _itemOffset - 1);
        }
        else _itemOffset = 0;
    }

    public void Draw(PixelRenderer renderer, RunWorld world, Vector2[] positions)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.ViewW, h = renderer.ViewH;
        if (!game.Interrupted)
        {
            string ping = game.LocalPing is int ms ? Localizer.Format("online.ping", Localizer.Format("online.ms", ms))
                : Localizer.Format("online.ping", "-");
            PixelFont.Draw(ui, ping, w - PixelFont.Measure(ping) - 4, 30, Foreground, Background);
            for (int slot = 0; slot < world.Players.Count; slot++)
            {
                var player = world.Players[slot];
                if (slot == world.Player.Id || (!player.Active && game.PlayerHero(slot) is null)) continue;
                string name = $"{slot + 1}. {game.PlayerName(slot)}";
                string health = !player.Active ? Localizer.Get("online.disconnected") :
                    player.Hp <= 0 ? Localizer.Get("online.downed") : $"{(int)MathF.Ceiling(player.Hp)}/{(int)MathF.Ceiling(RunWorld.MaxHealthFor(player))}";
                if (player.Active && game.Ping(slot) is int peerPing)
                    health += "  " + Localizer.Format("online.ms", peerPing);
                int row = 42 + slot * 20;
                PixelFont.Draw(ui, name, w - 4 - PixelFont.Measure(name), row, TeamColors[slot], Background);
                PixelFont.Draw(ui, health, w - 4 - PixelFont.Measure(health), row + 8, Foreground, Background);
                if (player.Active && player.Hp > 0)
                    DrawArrow(renderer, ui, positions[slot], slot);
            }
            PixelFont.DrawCentered(ui, Localizer.Get("online.teamHint"), w / 2, h - 12, Foreground, Background);
        }
        if (_tab) DrawItems(ui, world, w, h);
        if (_pauseMenu.IsOpen) _pauseMenu.Draw(renderer, canEnd: game.IsHost);
        else if (game.Paused)
            PixelFont.DrawCentered(ui, Localizer.Get("online.pausedByHost"), w / 2, h / 2 - 4, Foreground, Background);
    }

    private static void DrawArrow(PixelRenderer renderer, PixelCanvas ui, Vector2 position, int slot)
    {
        var (x, y) = renderer.WorldToUi(position.X, position.Y);
        int w = renderer.ViewW, h = renderer.ViewH;
        if (x >= 0 && x < w && y >= 0 && y < h) return;
        var center = new Vector2(w / 2f, h / 2f);
        var delta = new Vector2(x, y) - center;
        var direction = Vector2.Normalize(delta);
        var tangent = new Vector2(-direction.Y, direction.X);
        float edge = MathF.Min((center.X - 15) / MathF.Max(1, MathF.Abs(delta.X)),
            (center.Y - 22) / MathF.Max(1, MathF.Abs(delta.Y)));
        Vector2 tip = Vector2.Clamp(center + delta * edge + tangent * (slot - 1.5f) * 12,
            new Vector2(11, 14), new Vector2(w - 12, h - 15));
        Vector2 tail = tip - direction * 8;
        uint color = TeamColors[slot];
        ui.Line(tail.X, tail.Y, tip.X, tip.Y, color);
        Vector2 left = tip - direction * 5 + tangent * 4;
        Vector2 right = tip - direction * 5 - tangent * 4;
        ui.Line(left.X, left.Y, tip.X, tip.Y, color);
        ui.Line(right.X, right.Y, tip.X, tip.Y, color);
        PixelFont.DrawCentered(ui, (slot + 1).ToString(), (int)tail.X, (int)tail.Y + 3, color, Background);
    }

    private void DrawItems(PixelCanvas ui, RunWorld world, int w, int h)
    {
        int width = Math.Min(w - 16, 460), col = width / 2, rowHeight = (h - 54) / 2;
        int x = (w - width) / 2;
        Fill(ui, x, 18, width, rowHeight * 2 + 24, Background);
        PixelFont.DrawCentered(ui, Localizer.Get("online.teamItems"), w / 2, 23, Foreground, Background);
        int visibleRows = Math.Max(1, (rowHeight - 25) / 10);
        int longest = 0;
        foreach (var player in world.Players)
            if (player.Active) longest = Math.Max(longest, player.ItemStacks.Count(count => count > 0));
        _itemOffset = Math.Min(_itemOffset, Math.Max(0, longest - visibleRows));
        for (int slot = 0; slot < world.Players.Count; slot++)
        {
            var player = world.Players[slot];
            if (!player.Active) continue;
            int sx = x + 5 + slot % 2 * col, sy = 36 + slot / 2 * rowHeight;
            PixelFont.Draw(ui, Clip(game.PlayerName(slot), col - 10), sx, sy, TeamColors[slot], Background);
            int seen = 0, drawn = 0;
            for (int i = 0; i < player.ItemStacks.Length && drawn < visibleRows; i++)
            {
                if (player.ItemStacks[i] == 0) continue;
                if (seen++ < _itemOffset) continue;
                string count = $" x{player.ItemStacks[i]}";
                string item = Clip(Hud.ItemName(world.Content.Items[i]), col - 10 - PixelFont.Measure(count));
                PixelFont.Draw(ui, item + count, sx, sy + 12 + 10 * drawn++, Foreground, Background);
            }
            if (seen == 0) PixelFont.Draw(ui, Localizer.Get("online.noItems"), sx, sy + 12, Foreground, Background);
        }
        if (longest > visibleRows)
            PixelFont.DrawCentered(ui, Localizer.Get("online.scrollItems"), w / 2, 24 + rowHeight * 2, Foreground, Background);
    }

    private static void Fill(PixelCanvas ui, int x, int y, int w, int h, uint color)
    {
        for (int row = y; row < y + h; row++)
            for (int col = x; col < x + w; col++) ui.Set(col, row, color);
    }

    private static string Clip(string text, int width) => text[..Math.Min(Math.Max(0, width / 6), text.Length)];
}
