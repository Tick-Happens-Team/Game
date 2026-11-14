using System.Numerics;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

/// <summary>Screen-space HUD and overlays drawn into the renderer's UI layer, plus short-lived world rings.</summary>
internal sealed class Hud
{
    private const uint Ink = 0xFF22141Au, White = 0xFFEAECF4u, Teal = 0xFFDEF08Fu, TealDark = 0xFF6A8A1Fu, Gold = 0xFF3DC2FFu,
        Red = 0xFF2028C9u, RedDark = 0xFF17128Au, Grey = 0xFF857A6Cu, Panel = 0xFF331B2Bu;

    private struct Floater { public float X, Y, Age; public int Value; }
    private struct RingFx { public float X, Y, R, Age; }

    private readonly List<Floater> _numbers = [];
    private readonly List<RingFx> _rings = [];
    private string _notice = "";
    private uint _noticeColor;
    private float _noticeAge = 99;

    public void Reset()
    {
        _numbers.Clear();
        _rings.Clear();
        _noticeAge = 99;
    }

    public void DamageNumber(float x, float y, float value)
    {
        if (_numbers.Count >= 48) _numbers.RemoveAt(0);
        _numbers.Add(new Floater { X = x + (_numbers.Count % 5 - 2) * 2, Y = y, Value = (int)MathF.Ceiling(value) });
    }

    public void Notice(string text, uint color)
    {
        _notice = text;
        _noticeColor = color;
        _noticeAge = 0;
    }

    public void Ring(float x, float y, float r) => _rings.Add(new RingFx { X = x, Y = y, R = r });

    public void DrawWorldRings(PixelCanvas c, float dt)
    {
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var r = _rings[i];
            r.Age += dt;
            if (r.Age > 0.28f) { _rings.RemoveAt(i); continue; }
            _rings[i] = r;
            float k = r.Age / 0.28f, rad = r.R * (0.4f + 0.6f * k);
            int seg = (int)(rad * 5);
            for (int j = 0; j < seg; j++)
            {
                float a = j / (float)seg * MathF.Tau;
                c.Dot(r.X + MathF.Cos(a) * rad, r.Y + MathF.Sin(a) * rad * 0.62f, k < 0.5f ? Teal : TealDark, 1.2f - k);
            }
        }
    }

    public void Draw(PixelRenderer renderer, RunWorld world, Vector2 hero, float dt, bool paused)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.ViewW, h = renderer.ViewH;
        var p = world.Player;

        // Floating damage numbers (world anchored).
        for (int i = _numbers.Count - 1; i >= 0; i--)
        {
            var f = _numbers[i];
            if (!paused) f.Age += dt;
            if (f.Age > 0.6f) { _numbers.RemoveAt(i); continue; }
            _numbers[i] = f;
            var (sx, sy) = renderer.WorldToUi(f.X, f.Y - f.Age * 22);
            string s = f.Value.ToString();
            PixelFont.DrawCentered(ui, s, sx, sy, f.Age < 0.12f ? White : 0xFFA6F3FFu, Ink);
        }

        // HP and dash bars under the hero.
        var (hx, hy) = renderer.WorldToUi(hero.X, hero.Y + 3);
        float hpK = Math.Clamp(p.Hp / p.Stats[Stat.MaxHp], 0, 1);
        Bar(ui, hx - 9, hy, 18, 3, hpK, Red, RedDark);
        if (p.DashCooldownTicks > 0)
        {
            float k = 1 - p.DashCooldownTicks / (world.Rules.Dash.Cooldown * RunWorld.TicksPerSecond);
            Bar(ui, hx - 9, hy + 4, 18, 2, k, Teal, TealDark);
        }

        // Top: XP bar, level, run clock (with statue bonus), gold, kills.
        Bar(ui, 2, 2, w - 4, 5, p.Xp / p.XpNext, Teal, TealDark);
        PixelFont.Draw(ui, $"LV {p.Level}", 4, 10, White, Ink);
        int secs = (int)world.RunSeconds;
        PixelFont.DrawCentered(ui, $"{secs / 60:00}:{secs % 60:00}", w / 2, 10, White, Ink, 2);
        if (world.BonusSeconds > 0)
            PixelFont.DrawCentered(ui, $"STATUES +{(int)world.BonusSeconds / 60}:{(int)world.BonusSeconds % 60:00}", w / 2, 27, Teal, Ink);
        string right = $"GOLD {p.Wallet.Gold}  CHEST {p.Wallet.NextPrice}";
        PixelFont.Draw(ui, right, w - 4 - PixelFont.Measure(right), 10, Gold, Ink);
        string kills = $"KILLS {p.Kills}";
        PixelFont.Draw(ui, kills, w - 4 - PixelFont.Measure(kills), 20, Grey, Ink);

        // Bottom-left: loadout.
        int ly = h - 4 - PixelFont.LineHeight * (p.Weapons.Count + p.Items.Count);
        foreach (var wpn in p.Weapons)
        {
            PixelFont.Draw(ui, $"{world.Content.Weapons[wpn.Def].Name} {wpn.Level}", 4, ly, White, Ink);
            ly += PixelFont.LineHeight;
        }
        foreach (var it in p.Items)
        {
            PixelFont.Draw(ui, $"{world.Content.Items[it.Def].Name} {it.Level}", 4, ly, Grey, Ink);
            ly += PixelFont.LineHeight;
        }

        // Chest hint and notices.
        if (world.NearbyChest >= 0)
        {
            var (cx, cy) = renderer.WorldToUi(world.Chests[world.NearbyChest].X, world.Chests[world.NearbyChest].Y - 26);
            PixelFont.DrawCentered(ui, $"NEED {p.Wallet.NextPrice} GOLD", cx, cy, Gold, Ink);
        }
        if (_noticeAge < 2.6f)
        {
            if (!paused) _noticeAge += dt;
            PixelFont.DrawCentered(ui, _notice, w / 2, 40, _noticeColor, Ink);
        }
        else if (!paused) _noticeAge += dt;

        if (world.Phase == RunPhase.LevelUp) DrawLevelUp(ui, world, w, h);
        else if (world.Phase == RunPhase.Dead) DrawDeath(ui, world, w, h);
        else if (paused) { Dim(ui, w, h, 0.5f); PixelFont.DrawCentered(ui, "PAUSED", w / 2, h / 2 - 8, White, Ink, 2); PixelFont.DrawCentered(ui, "P - RESUME   ESC - QUIT", w / 2, h / 2 + 12, Grey, Ink); }
    }

    private static void Bar(PixelCanvas ui, int x, int y, int width, int height, float k, uint fill, uint back)
    {
        k = Math.Clamp(k, 0, 1);
        for (int yy = -1; yy <= height; yy++)
            for (int xx = -1; xx <= width; xx++)
            {
                bool border = yy < 0 || xx < 0 || yy == height || xx == width;
                ui.Set(x + xx, y + yy, border ? Ink : xx < width * k ? (yy == 0 ? Brighten(fill) : fill) : back);
            }
    }

    private static uint Brighten(uint c) => VfxMath.Mix(c, 0xFFFFFFFFu, 0.35f);

    private static void Dim(PixelCanvas ui, int w, int h, float a)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) ui.Blend(x, y, 0xFF140A10u, a);
    }

    private static void Box(PixelCanvas ui, int x, int y, int bw, int bh, uint border)
    {
        for (int yy = 0; yy < bh; yy++)
            for (int xx = 0; xx < bw; xx++)
            {
                bool edge = xx == 0 || yy == 0 || xx == bw - 1 || yy == bh - 1;
                ui.Set(x + xx, y + yy, edge ? border : Panel);
            }
    }

    private static void DrawLevelUp(PixelCanvas ui, RunWorld world, int w, int h)
    {
        Dim(ui, w, h, 0.55f);
        PixelFont.DrawCentered(ui, "LEVEL UP!", w / 2, h / 2 - 78, Teal, Ink, 2);
        PixelFont.DrawCentered(ui, "CHOOSE: 1  2  3", w / 2, h / 2 - 58, Grey, Ink);
        int count = world.Offer.Count, cw = 150, ch = 96, gap = 10;
        int x0 = w / 2 - (count * cw + (count - 1) * gap) / 2, y0 = h / 2 - 40;
        for (int i = 0; i < count; i++)
        {
            var parts = world.Describe(world.Offer[i]).Split('|');
            int x = x0 + i * (cw + gap);
            bool isNew = world.Offer[i].Kind is OfferKind.NewWeapon or OfferKind.NewItem;
            Box(ui, x, y0, cw, ch, isNew ? Gold : Teal);
            PixelFont.Draw(ui, $"[{i + 1}]", x + 6, y0 + 6, Grey, Ink);
            PixelFont.Draw(ui, parts[0], x + 6, y0 + 18, White, Ink);
            PixelFont.Draw(ui, parts[1], x + 6, y0 + 28, isNew ? Gold : Teal, Ink);
            int ly = y0 + 42;
            foreach (var line in Wrap(parts[2], (cw - 12) / PixelFont.Advance))
            {
                PixelFont.Draw(ui, line, x + 6, ly, Grey, Ink);
                ly += PixelFont.LineHeight;
            }
        }
    }

    private static void DrawDeath(PixelCanvas ui, RunWorld world, int w, int h)
    {
        Dim(ui, w, h, 0.65f);
        var p = world.Player;
        int secs = (int)(world.Tick * RunWorld.Dt);
        PixelFont.DrawCentered(ui, "YOU FELL", w / 2, h / 2 - 50, 0xFF6060FFu, Ink, 3);
        PixelFont.DrawCentered(ui, $"SURVIVED {secs / 60:00}:{secs % 60:00}   RUN CLOCK {(int)world.RunSeconds / 60:00}:{(int)world.RunSeconds % 60:00}", w / 2, h / 2 - 12, White, Ink);
        PixelFont.DrawCentered(ui, $"LEVEL {p.Level}   KILLS {p.Kills}   GOLD {p.Wallet.Gold}", w / 2, h / 2 + 2, White, Ink);
        PixelFont.DrawCentered(ui, "R - NEW RUN   ESC - QUIT", w / 2, h / 2 + 26, Teal, Ink);
    }

    private static IEnumerable<string> Wrap(string text, int maxChars)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars) { yield return line; line = word; }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) yield return line;
    }
}
