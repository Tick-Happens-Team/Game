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
    private const uint Background = 0xFF151018, Foreground = 0xFFF4F1FF;
    private readonly PauseMenu _pauseMenu = new(settings, online: true);
    public bool CapturesInput => _pauseMenu.IsOpen;

    public void Reset()
    {
        _pauseMenu.Reset();
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

    public void Draw(PixelRenderer renderer, RunWorld world, Vector2[] positions)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.UiW, h = renderer.UiH;
        if (!game.Interrupted)
        {
            string ping = game.LocalPing is int ms ? Localizer.Format("online.ping", Localizer.Format("online.ms", ms))
                : Localizer.Format("online.ping", "-");
            PixelFont.Draw(ui, ping, w - PixelFont.Measure(ping) - 6, Hud.RunHeaderBottom + 5, Foreground, Background);
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
                PixelFont.Draw(ui, name, w - 4 - PixelFont.Measure(name), row, TeamColors[slot], Background);
                PixelFont.Draw(ui, health, w - 4 - PixelFont.Measure(health), row + 8, Foreground, Background);
                if (player.Active && player.Hp > 0)
                    DrawArrow(renderer, ui, positions[slot], slot);
            }
            PixelFont.DrawCentered(ui, Localizer.Get("online.teamHint"), w / 2, h - 12, Foreground, Background);
        }
        if (world.Phase == RunPhase.Dead) return;
        if (_pauseMenu.IsOpen) _pauseMenu.Draw(renderer, canEnd: game.IsHost);
        else if (game.Paused)
            PixelFont.DrawCentered(ui, Localizer.Get("online.pausedByHost"), w / 2, h / 2 - 4, Foreground, Background);
    }

    private static void DrawArrow(PixelRenderer renderer, PixelCanvas ui, Vector2 position, int slot)
    {
        var (x, y) = renderer.WorldToUi(position.X, position.Y);
        int w = renderer.UiW, h = renderer.UiH;
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
}
