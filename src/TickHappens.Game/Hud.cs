using System.Globalization;
using System.Numerics;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Game;

/// <summary>Screen-space HUD, character select and overlays drawn into the renderer's UI layer, plus short-lived world rings.</summary>
internal sealed class Hud
{
    private const uint Ink = 0xFF22141Au, White = 0xFFEAECF4u, Teal = 0xFFDEF08Fu, TealDark = 0xFF6A8A1Fu, Gold = 0xFF3DC2FFu,
        Red = 0xFF2028C9u, RedDark = 0xFF17128Au, Grey = 0xFF857A6Cu, Panel = 0xFF331B2Bu;

    private struct Floater { public float X, Y, Age; public int Value; public uint Color; }
    private struct RingFx { public float X, Y, R, Age; }

    private readonly List<Floater> _numbers = [];
    private readonly List<RingFx> _rings = [];
    private string _notice = "";
    private uint _noticeColor;
    private float _noticeAge = 99;

    public static uint RarityColor(string rarity) => rarity switch
    {
        "rare" => 0xFF4AA86Au,
        "epic" => 0xFFC070E0u,
        "mythic" => 0xFF1D56F2u,
        "legendary" => 0xFF3DC2FFu,
        _ => 0xFFCFC6D4u,
    };

    public static string RarityName(string rarity) => rarity switch
    {
        "rare" => "РЕДКИЙ",
        "epic" => "ЭПИЧЕСКИЙ",
        "mythic" => "МИФИЧЕСКИЙ",
        "legendary" => "ЛЕГЕНДАРНЫЙ",
        _ => "ОБЫЧНЫЙ",
    };

    public void Reset()
    {
        _numbers.Clear();
        _rings.Clear();
        _noticeAge = 99;
    }

    public void DamageNumber(float x, float y, float value, DamageKind kind)
    {
        if (_numbers.Count >= 48) _numbers.RemoveAt(0);
        uint color = kind switch { DamageKind.Magical => 0xFFFFB0D0u, DamageKind.Pure => White, _ => 0xFFA6F3FFu };
        _numbers.Add(new Floater { X = x + (_numbers.Count % 5 - 2) * 2, Y = y, Value = (int)MathF.Ceiling(value), Color = color });
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

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Pct(float v) => (v * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public void Draw(PixelRenderer renderer, RunWorld world, Vector2 hero, float dt, bool paused)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.ViewW, h = renderer.ViewH;
        var p = world.Player;

        // Floating damage numbers (world anchored), colored by damage type.
        for (int i = _numbers.Count - 1; i >= 0; i--)
        {
            var f = _numbers[i];
            if (!paused) f.Age += dt;
            if (f.Age > 0.6f) { _numbers.RemoveAt(i); continue; }
            _numbers[i] = f;
            var (sx, sy) = renderer.WorldToUi(f.X, f.Y - f.Age * 22);
            PixelFont.DrawCentered(ui, f.Value.ToString(CultureInfo.InvariantCulture), sx, sy, f.Age < 0.12f ? White : f.Color, Ink);
        }

        // HP and dash bars under the hero.
        var (hx, hy) = renderer.WorldToUi(hero.X, hero.Y + 3);
        Bar(ui, hx - 9, hy, 18, 3, p.Hp / MathF.Max(1, world.MaxHealth), Red, RedDark);
        if (p.DashCooldownTicks > 0)
        {
            float full = world.Rules.Dash.Cooldown * p.Stats[Stat.DashCooldown] * p.Stats[Stat.Cooldown] * RunWorld.TicksPerSecond;
            Bar(ui, hx - 9, hy + 4, 18, 2, 1 - p.DashCooldownTicks / MathF.Max(1, full), Teal, TealDark);
        }

        // Top: XP bar, hero and level, run clock (with statue bonus), gold and chest price, kills.
        Bar(ui, 2, 2, w - 4, 5, p.Xp / p.XpNext, Teal, TealDark);
        PixelFont.Draw(ui, $"{p.Character.Name}  УР {p.Level}", 4, 10, White, Ink);
        PixelFont.Draw(ui, $"ХП {(int)MathF.Ceiling(p.Hp)}/{(int)MathF.Ceiling(world.MaxHealth)}", 4, 20, 0xFF7070FFu, Ink);
        int secs = (int)world.RunSeconds;
        PixelFont.DrawCentered(ui, $"{secs / 60:00}:{secs % 60:00}", w / 2, 10, White, Ink, 2);
        if (world.BonusSeconds > 0)
            PixelFont.DrawCentered(ui, $"СТАТУИ +{(int)world.BonusSeconds / 60}:{(int)world.BonusSeconds % 60:00}", w / 2, 27, Teal, Ink);
        string right = $"ЗОЛОТО {p.Wallet.Gold}  СУНДУК {p.Wallet.NextPrice}";
        PixelFont.Draw(ui, right, w - 4 - PixelFont.Measure(right), 10, Gold, Ink);
        string kills = $"УБИТО {p.Kills}";
        PixelFont.Draw(ui, kills, w - 4 - PixelFont.Measure(kills), 20, Grey, Ink);

        // Bottom-left: items with stacks, colored by rarity.
        int lines = 0;
        for (int i = 0; i < p.ItemStacks.Length; i++) if (p.ItemStacks[i] > 0) lines++;
        int ly = h - 4 - PixelFont.LineHeight * lines;
        for (int i = 0; i < p.ItemStacks.Length; i++)
        {
            if (p.ItemStacks[i] == 0) continue;
            var it = world.Content.Items[i];
            PixelFont.Draw(ui, p.ItemStacks[i] > 1 ? $"{it.Name} ×{p.ItemStacks[i]}" : it.Name, 4, ly, RarityColor(it.Rarity), Ink);
            ly += PixelFont.LineHeight;
        }

        // Chest hint and notices.
        if (world.NearbyChest >= 0)
        {
            var (cx, cy) = renderer.WorldToUi(world.Chests[world.NearbyChest].X, world.Chests[world.NearbyChest].Y - 26);
            PixelFont.DrawCentered(ui, $"НУЖНО {p.Wallet.NextPrice} ЗОЛОТА", cx, cy, Gold, Ink);
        }
        if (_noticeAge < 2.6f)
        {
            if (!paused) _noticeAge += dt;
            PixelFont.DrawCentered(ui, _notice, w / 2, 40, _noticeColor, Ink);
        }

        if (world.Phase == RunPhase.Dead) DrawDeath(ui, world, w, h);
        else if (paused)
        {
            Dim(ui, w, h, 0.5f);
            PixelFont.DrawCentered(ui, "ПАУЗА", w / 2, h / 2 - 8, White, Ink, 2);
            PixelFont.DrawCentered(ui, "P - ПРОДОЛЖИТЬ   R - НОВЫЙ ЗАБЕГ   ESC - ВЫХОД", w / 2, h / 2 + 12, Grey, Ink);
        }
    }

    /// <summary>Character select: list on the left, the highlighted hero's table stats, passive and growth on the right.</summary>
    public void DrawMenu(PixelRenderer renderer, ContentSet content, int selected)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.ViewW, h = renderer.ViewH;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x < w / 2 - 40 || x > w / 2 + 40 || y < h / 2 - 50 || y > h / 2 + 20) ui.Blend(x, y, 0xFF140A10u, 0.55f);
        PixelFont.DrawCentered(ui, "TICKHAPPENS", w / 2, 12, Teal, Ink, 2);
        PixelFont.DrawCentered(ui, "ВЫБОР ГЕРОЯ", w / 2, 32, Grey, Ink);

        int lx = 16, ly = 60;
        for (int i = 0; i < content.Characters.Count; i++)
        {
            bool sel = i == selected;
            if (sel) for (int y = -2; y < PixelFont.LineHeight; y++) for (int x = -4; x < 130; x++) ui.Set(lx + x, ly + y, Panel);
            PixelFont.Draw(ui, (sel ? "> " : "  ") + content.Characters[i].Name, lx, ly, sel ? White : Grey, Ink);
            ly += PixelFont.LineHeight + 4;
        }

        var c = content.Characters[selected];
        var s = c.Stats;
        int px = w - 250, py = 60, pw = 236;
        Box(ui, px - 8, py - 8, pw + 16, h - py - 30, Teal);
        PixelFont.Draw(ui, c.Name, px, py, White, Ink, 2);
        py += 22;
        PixelFont.Draw(ui, $"ХП {F(s.MaxHp)}/+{F(s.MaxHpPerLevel)}   УРОН {F(s.Damage)}/+{F(s.DamagePerLevel)}", px, py, White, Ink);
        py += PixelFont.LineHeight + 2;
        PixelFont.Draw(ui, $"С/П {F(s.MoveSpeed)}   С/А {F(s.AttackSpeed)}", px, py, White, Ink);
        py += PixelFont.LineHeight + 2;
        PixelFont.Draw(ui, $"БРОНЯ {Pct(s.Armor)}   М/З {Pct(s.MagicResist)}", px, py, White, Ink);
        py += PixelFont.LineHeight + 8;
        PixelFont.Draw(ui, "ПАССИВКА", px, py, Teal, Ink);
        py += PixelFont.LineHeight + 1;
        foreach (var line in Wrap(c.Passive, pw / PixelFont.Advance))
        {
            PixelFont.Draw(ui, line, px, py, Grey, Ink);
            py += PixelFont.LineHeight;
        }
        py += 6;
        PixelFont.Draw(ui, "РОСТ ЗА УРОВЕНЬ", px, py, Teal, Ink);
        py += PixelFont.LineHeight + 1;
        foreach (var line in Wrap(c.Growth, pw / PixelFont.Advance))
        {
            PixelFont.Draw(ui, line, px, py, Grey, Ink);
            py += PixelFont.LineHeight;
        }
        PixelFont.DrawCentered(ui, "A/D ИЛИ СТРЕЛКИ - ВЫБОР    ПРОБЕЛ - НАЧАТЬ    ESC - ВЫХОД", w / 2, h - 14, Teal, Ink);
    }

    private static void Bar(PixelCanvas ui, int x, int y, int width, int height, float k, uint fill, uint back)
    {
        k = Math.Clamp(k, 0, 1);
        for (int yy = -1; yy <= height; yy++)
            for (int xx = -1; xx <= width; xx++)
            {
                bool border = yy < 0 || xx < 0 || yy == height || xx == width;
                ui.Set(x + xx, y + yy, border ? Ink : xx < width * k ? (yy == 0 ? VfxMath.Mix(fill, 0xFFFFFFFFu, 0.35f) : fill) : back);
            }
    }

    private static void Dim(PixelCanvas ui, int w, int h, float a)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) ui.Blend(x, y, 0xFF140A10u, a);
    }

    private static void Box(PixelCanvas ui, int x, int y, int bw, int bh, uint border)
    {
        for (int yy = 0; yy < bh; yy++)
            for (int xx = 0; xx < bw; xx++)
                ui.Set(x + xx, y + yy, xx == 0 || yy == 0 || xx == bw - 1 || yy == bh - 1 ? border : Panel);
    }

    private static void DrawDeath(PixelCanvas ui, RunWorld world, int w, int h)
    {
        Dim(ui, w, h, 0.65f);
        var p = world.Player;
        int secs = (int)(world.Tick * RunWorld.Dt);
        PixelFont.DrawCentered(ui, "ГЕРОЙ ПАЛ", w / 2, h / 2 - 50, 0xFF6060FFu, Ink, 3);
        PixelFont.DrawCentered(ui, $"ПРОДЕРЖАЛСЯ {secs / 60:00}:{secs % 60:00}   ЧАСЫ ЗАБЕГА {(int)world.RunSeconds / 60:00}:{(int)world.RunSeconds % 60:00}", w / 2, h / 2 - 12, White, Ink);
        PixelFont.DrawCentered(ui, $"{p.Character.Name}   УРОВЕНЬ {p.Level}   УБИТО {p.Kills}   ЗОЛОТО {p.Wallet.Gold}", w / 2, h / 2 + 2, White, Ink);
        PixelFont.DrawCentered(ui, "R - НОВЫЙ ЗАБЕГ   ESC - ВЫХОД", w / 2, h / 2 + 26, Teal, Ink);
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
