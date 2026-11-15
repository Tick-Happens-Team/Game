using TickHappens.Localization;
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
        "cursed" => 0xFFFFA168u,
        _ => 0xFFCFC6D4u,
    };

    public static string RarityName(string rarity) => rarity switch
    {
        "rare" => Localizer.Get("rarity.rare"),
        "epic" => Localizer.Get("rarity.epic"),
        "mythic" => Localizer.Get("rarity.mythic"),
        "legendary" => Localizer.Get("rarity.legendary"),
        "cursed" => Localizer.Get("rarity.cursed"),
        _ => Localizer.Get("rarity.common"),
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

    private static string F(float v) => Localizer.Number(v, "0.##");
    private static string Pct(float v) => Localizer.Number(v, "0.##%");

    internal static string CharacterName(CharacterDef character) =>
        Localizer.Content("characters", character.Id, "name", string.IsNullOrEmpty(character.Name) ? character.Id : character.Name);

    public static string ItemName(ItemDef item) =>
        Localizer.Content("items", item.Id, "name", string.IsNullOrEmpty(item.Name) ? item.Id : item.Name);

    public void Draw(PixelRenderer renderer, RunWorld world, Vector2 hero, float dt, bool paused, bool online, string? interruption, bool canReconnect)
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
        if (p.Shield + p.BonusShield > 0)
            Bar(ui, hx - 9, hy - 3, 18, 1, (p.Shield + p.BonusShield) / MathF.Max(1, world.MaxHealth), Teal, TealDark);
        if (p.DashCooldownTicks > 0)
        {
            float full = world.Rules.Dash.Cooldown * p.Stats[Stat.DashCooldown] * p.Stats[Stat.Cooldown] * RunWorld.TicksPerSecond;
            Bar(ui, hx - 9, hy + 4, 18, 2, 1 - p.DashCooldownTicks / MathF.Max(1, full), Teal, TealDark);
        }

        int dashCapacity = RunWorld.DashChargeCapacity(p);
        if (dashCapacity > 1)
            for (int i = 0; i < dashCapacity; i++)
                ui.Set(hx - dashCapacity + i * 2, hy + 8, i < dashCapacity - p.DashChargesUsed ? Teal : Grey);

        // Top: XP bar, hero and level, run clock (with statue bonus), gold and chest price, kills.
        Bar(ui, 2, 2, w - 4, 5, p.Xp / p.XpNext, Teal, TealDark);
        PixelFont.Draw(ui, Localizer.Format("hud.level", CharacterName(p.Character), p.Level), 4, 10, White, Ink);
        PixelFont.Draw(ui, Localizer.Format("hud.hp", (int)MathF.Ceiling(p.Hp), (int)MathF.Ceiling(world.MaxHealth)), 4, 20, 0xFF7070FFu, Ink);
        int secs = (int)world.RunSeconds;
        PixelFont.DrawCentered(ui, Localizer.Format("hud.time", secs / 60, secs % 60), w / 2, 10, White, Ink, 2);
        if (world.BonusSeconds > 0)
            PixelFont.DrawCentered(ui, Localizer.Format("hud.statues", (int)world.BonusSeconds / 60, (int)world.BonusSeconds % 60), w / 2, 27, Teal, Ink);
        string right = Localizer.Format("hud.gold", p.Wallet.Gold, p.Wallet.NextPrice);
        PixelFont.Draw(ui, right, w - 4 - PixelFont.Measure(right), 10, Gold, Ink);
        string kills = Localizer.Format("hud.kills", p.Kills);
        PixelFont.Draw(ui, kills, w - 4 - PixelFont.Measure(kills), 20, Grey, Ink);

        // Bottom-left: items with stacks, colored by rarity.
        int lines = 0;
        for (int i = 0; i < p.ItemStacks.Length; i++) if (p.ItemStacks[i] > 0) lines++;
        int ly = h - 4 - PixelFont.LineHeight * lines;
        for (int i = 0; i < p.ItemStacks.Length; i++)
        {
            if (p.ItemStacks[i] == 0) continue;
            var it = world.Content.Items[i];
            PixelFont.Draw(ui, p.ItemStacks[i] > 1 ? Localizer.Format("hud.stack", ItemName(it), p.ItemStacks[i]) : ItemName(it), 4, ly, RarityColor(it.Rarity), Ink);
            ly += PixelFont.LineHeight;
        }

        // Chest hint and notices.
        if (world.NearbyChest >= 0)
        {
            var (cx, cy) = renderer.WorldToUi(world.Chests[world.NearbyChest].X, world.Chests[world.NearbyChest].Y - 26);
            PixelFont.DrawCentered(ui, Localizer.Format("hud.needGold", p.Wallet.NextPrice), cx, cy, Gold, Ink);
        }
        if (p.Hp > 0 && p.ChestOfferFirst >= 0) DrawChestOffer(ui, world, w);
        else if (_noticeAge < 2.6f)
        {
            if (!paused) _noticeAge += dt;
            PixelFont.DrawCentered(ui, _notice, w / 2, 40, _noticeColor, Ink);
        }

        if (world.Phase == RunPhase.Dead || interruption is not null)
            DrawResults(ui, world, w, h, online, interruption, canReconnect);
        else if (paused) Dim(ui, w, h, 0.5f);
    }

    private static void DrawChestOffer(PixelCanvas ui, RunWorld world, int width)
    {
        var p = world.Player;
        int left = Math.Max(0, p.ChestOfferUntilTick - world.Tick);
        int seconds = (left + RunWorld.TicksPerSecond - 1) / RunWorld.TicksPerSecond;
        int count = p.ChestOfferThird >= 0 ? 3 : 2;
        int cardWidth = Math.Min(220, (width - 20) / count);
        int totalWidth = cardWidth * count + (count - 1) * 4;
        int x = (width - totalWidth) / 2;
        PixelFont.DrawCentered(ui, Localizer.Format("hud.chestChoice", seconds), width / 2, 43, Gold, Ink);
        Bar(ui, x + 1, 53, totalWidth - 2, 2, (float)left / RunWorld.ChestChoiceTicks, Gold, Panel);
        DrawChestCard(ui, world.Content.Items[p.ChestOfferFirst], 1, x, cardWidth);
        DrawChestCard(ui, world.Content.Items[p.ChestOfferSecond], 2, x + cardWidth + 4, cardWidth);
        if (count == 3) DrawChestCard(ui, world.Content.Items[p.ChestOfferThird], 3, x + (cardWidth + 4) * 2, cardWidth);
    }

    private static void DrawChestCard(PixelCanvas ui, ItemDef item, int choice, int x, int width)
    {
        const int top = 58;
        int columns = Math.Max(1, (width - 12) / PixelFont.Advance);
        string title = $"[{choice}] {ItemName(item)}";
        string description = Localizer.Content("items", item.Id, "description", item.Description);
        var titleLines = Wrap(title, columns).ToArray();
        var descriptionLines = Wrap(description, columns).ToArray();
        uint rarityColor = RarityColor(item.Rarity);
        Box(ui, x, top, width, 12 + (titleLines.Length + descriptionLines.Length) * PixelFont.LineHeight, rarityColor);
        int y = top + 5;
        foreach (string line in titleLines)
        {
            PixelFont.Draw(ui, line, x + 6, y, rarityColor, Ink);
            y += PixelFont.LineHeight;
        }
        y += 2;
        foreach (string line in descriptionLines)
        {
            PixelFont.Draw(ui, line, x + 6, y, White, Ink);
            y += PixelFont.LineHeight;
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
        PixelFont.DrawCentered(ui, Localizer.Get("app.title"), w / 2, 12, Teal, Ink, 2);
        PixelFont.DrawCentered(ui, Localizer.Get("menu.pick"), w / 2, 32, Grey, Ink);

        int lx = 16, ly = 60;
        for (int i = 0; i < content.Characters.Count; i++)
        {
            bool sel = i == selected;
            if (sel) for (int y = -2; y < PixelFont.LineHeight; y++) for (int x = -4; x < 130; x++) ui.Set(lx + x, ly + y, Panel);
            PixelFont.Draw(ui, Localizer.Format("menu.row", sel ? ">" : " ", CharacterName(content.Characters[i])), lx, ly, sel ? White : Grey, Ink);
            ly += PixelFont.LineHeight + 4;
        }

        var c = content.Characters[selected];
        var s = c.Stats;
        int px = w - 250, py = 60, pw = 236;
        Box(ui, px - 8, py - 8, pw + 16, h - py - 30, Teal);
        PixelFont.Draw(ui, CharacterName(c), px, py, White, Ink, 2);
        py += 22;
        PixelFont.Draw(ui, Localizer.Format("menu.healthDamage", F(s.MaxHp), F(s.MaxHpPerLevel), F(s.Damage), F(s.DamagePerLevel)), px, py, White, Ink);
        py += PixelFont.LineHeight + 2;
        PixelFont.Draw(ui, Localizer.Format("menu.speed", F(s.MoveSpeed), F(s.AttackSpeed)), px, py, White, Ink);
        py += PixelFont.LineHeight + 2;
        PixelFont.Draw(ui, Localizer.Format("menu.resist", Pct(s.Armor), Pct(s.MagicResist)), px, py, White, Ink);
        py += PixelFont.LineHeight + 8;
        PixelFont.Draw(ui, Localizer.Get("menu.passive"), px, py, Teal, Ink);
        py += PixelFont.LineHeight + 1;
        foreach (var line in Wrap(Localizer.Content("characters", c.Id, "passive", c.Passive), pw / PixelFont.Advance))
        {
            PixelFont.Draw(ui, line, px, py, Grey, Ink);
            py += PixelFont.LineHeight;
        }
        py += 6;
        PixelFont.Draw(ui, Localizer.Get("menu.growth"), px, py, Teal, Ink);
        py += PixelFont.LineHeight + 1;
        foreach (var line in Wrap(Localizer.Content("characters", c.Id, "growth", c.Growth), pw / PixelFont.Advance))
        {
            PixelFont.Draw(ui, line, px, py, Grey, Ink);
            py += PixelFont.LineHeight;
        }
        PixelFont.DrawCentered(ui, Localizer.Format("menu.language", Localizer.Get("language." + Localizer.Language)),
            w / 2, h - 50, White, Ink);
        PixelFont.DrawCentered(ui, Localizer.Get("menu.help"), w / 2, h - 14, Teal, Ink);
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

    private static void DrawResults(PixelCanvas ui, RunWorld world, int w, int h, bool online, string? interruption, bool canReconnect)
    {
        Dim(ui, w, h, 0.65f);
        var p = world.Player;
        int secs = (int)(world.Tick * RunWorld.Dt);
        PixelFont.DrawCentered(ui, interruption ?? Localizer.Get("hud.fallen"),
            w / 2, h / 2 - 50, 0xFF6060FFu, Ink, interruption is null ? 3 : 1);
        PixelFont.DrawCentered(ui, Localizer.Format("hud.summaryTime", secs / 60, secs % 60, (int)world.RunSeconds / 60, (int)world.RunSeconds % 60), w / 2, h / 2 - 12, White, Ink);
        PixelFont.DrawCentered(ui, Localizer.Format("hud.summary", CharacterName(p.Character), p.Level, p.Kills, p.Wallet.Gold), w / 2, h / 2 + 2, White, Ink);
        PixelFont.DrawCentered(ui, online ? Localizer.Get("hud.leave") : Localizer.Get("hud.restart"),
            w / 2, h / 2 + 26, Teal, Ink);
        if (canReconnect)
            PixelFont.DrawCentered(ui, Localizer.Get("online.reconnect"), w / 2, h / 2 + 40, Teal, Ink);
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
