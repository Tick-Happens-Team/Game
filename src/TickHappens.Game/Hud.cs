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
internal sealed class Hud(GameSettings settings)
{
    private const uint Ink = 0xFF22141Au, White = 0xFFEAECF4u, Teal = 0xFFDEF08Fu, TealDark = 0xFF6A8A1Fu, Gold = 0xFF3DC2FFu,
        Red = 0xFF2028C9u, RedDark = 0xFF17128Au, Grey = 0xFF857A6Cu, Panel = 0xFF331B2Bu,
        PanelLit = 0xFF4A2A40u, PanelEdge = 0xFF5A4050u;

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
        int w = renderer.UiW, h = renderer.UiH;
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
        if (p.Character.Id == "necromancer")
            PixelFont.Draw(ui, Localizer.Format("hud.souls", p.Souls), 4, 30, Teal, Ink);
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

    private void DrawChestOffer(PixelCanvas ui, RunWorld world, int width)
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
        DrawChestCard(ui, world.Content.Items[p.ChestOfferFirst], RunAction.Chest1, x, cardWidth);
        DrawChestCard(ui, world.Content.Items[p.ChestOfferSecond], RunAction.Chest2, x + cardWidth + 4, cardWidth);
        if (count == 3)
            DrawChestCard(ui, world.Content.Items[p.ChestOfferThird], RunAction.Chest3, x + (cardWidth + 4) * 2, cardWidth);
    }

    private void DrawChestCard(PixelCanvas ui, ItemDef item, RunAction choice, int x, int width)
    {
        const int top = 58;
        int columns = Math.Max(1, (width - 12) / PixelFont.Advance);
        string title = $"[{GameSettings.KeyLabel(settings.Keys[(int)choice])}] {ItemName(item)}";
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

    // ---------------------------------------------------------------- character screen
    private const int RosterX = 14, RosterY = 54, RosterW = 156, RowH = 30;
    private const int PanelW = 252, StartW = 120, StartH = 18;
    private float _menuTime;

    private static int PanelX(int w) => w - PanelW - 14;
    private static int StageX(int w) => (RosterX + RosterW + PanelX(w)) / 2;
    // Under the info panel, clear of the settings and online buttons at the bottom centre.
    private static (int X, int Y) StartButton(int w, int h) => (PanelX(w) + PanelW - StartW, h - 66);

    private static int RosterRows(int height) => Math.Max(1, (height - RosterY - 65) / RowH);

    /// <summary>Roster row under a normalized window point, or -1.</summary>
    public static int MenuRowAt(PixelRenderer renderer, (float X, float Y)? point, int count, int selected)
    {
        if (point is not { } p) return -1;
        float x = p.X * renderer.UiW, y = p.Y * renderer.UiH;
        if (x < RosterX || x >= RosterX + RosterW || y < RosterY) return -1;
        int row = (int)((y - RosterY) / RowH);
        int rows = RosterRows(renderer.UiH);
        if (row >= rows || (y - RosterY) % RowH >= RowH - 3) return -1;
        int index = (selected / rows) * rows + row;
        return index < count ? index : -1;
    }

    public static int MenuPageAt(PixelRenderer renderer, (float X, float Y)? point, int count, int selected)
    {
        if (point is not { } p) return -1;
        int rows = RosterRows(renderer.UiH);
        int first = selected / rows * rows;
        int y = RosterY + rows * RowH;
        float mouseY = p.Y * renderer.UiH;
        if (mouseY < y || mouseY >= y + 16) return -1;
        float x = p.X * renderer.UiW;
        if (first > 0 && x >= RosterX && x < RosterX + 24) return first - rows;
        if (first + rows < count && x >= RosterX + RosterW - 24 && x < RosterX + RosterW) return first + rows;
        return -1;
    }

    /// <summary>True when a normalized window point is on the start button.</summary>
    public static bool MenuStartAt(PixelRenderer renderer, (float X, float Y)? point)
    {
        if (point is not { } p) return false;
        var (bx, by) = StartButton(renderer.UiW, renderer.UiH);
        float x = p.X * renderer.UiW, y = p.Y * renderer.UiH;
        return x >= bx && x < bx + StartW && y >= by && y < by + StartH;
    }

    /// <summary>
    /// Character select: a roster on the left, the selected hero's own sprite enlarged on a lit pedestal in the middle,
    /// and its table stats, passive and growth on the right.
    /// </summary>
    public void DrawMenu(PixelRenderer renderer, ContentSet content, int selected, HeroPreview preview, float frameDt, (float X, float Y)? mouse)
    {
        _menuTime += frameDt;
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        int w = renderer.UiW, h = renderer.UiH;
        Dim(ui, w, h, 0.78f);
        DrawMenuBackdrop(ui, w, h);

        PixelFont.DrawCentered(ui, Localizer.Get("app.title"), w / 2, 12, Teal, Ink, 2);
        PixelFont.DrawCentered(ui, Localizer.Get("menu.pick"), w / 2, 32, Grey, Ink);

        // Roster: the hero's sprite and name per row; the selected hero animates, the mouse highlights a row.
        int rows = RosterRows(h);
        int first = (selected / rows) * rows;
        int last = Math.Min(first + rows, content.Characters.Count);
        int hover = MenuRowAt(renderer, mouse, content.Characters.Count, selected);
        for (int i = first; i < last; i++)
        {
            int y = RosterY + (i - first) * RowH;
            bool sel = i == selected;
            Frame(ui, RosterX, y, RosterW, RowH - 3, sel ? Teal : i == hover ? TealDark : PanelEdge, sel ? PanelLit : Panel);
            preview.DrawHero(ui, i, sel ? _menuTime : 0, RosterX + 14, y + 25, 1);
            PixelFont.Draw(ui, CharacterName(content.Characters[i]), RosterX + 32, y + 10, sel ? White : Grey, Ink);
        }

        if (content.Characters.Count > rows)
        {
            int y = RosterY + rows * RowH;
            PixelFont.DrawCentered(ui, $"{first + 1}-{last} / {content.Characters.Count}", RosterX + RosterW / 2, y + 4, Grey, Ink);
            int pageHover = MenuPageAt(renderer, mouse, content.Characters.Count, selected);
            if (first > 0)
            {
                Frame(ui, RosterX, y, 24, 16, Teal, pageHover == first - rows ? PanelLit : Panel);
                PixelFont.DrawCentered(ui, "<", RosterX + 12, y + 4, Teal, Ink);
            }
            if (last < content.Characters.Count)
            {
                int x = RosterX + RosterW - 24;
                Frame(ui, x, y, 24, 16, Teal, pageHover == first + rows ? PanelLit : Panel);
                PixelFont.DrawCentered(ui, ">", x + 12, y + 4, Teal, Ink);
            }
        }

        // Stage: the hero's in-game sprite, five times larger, on a glowing pedestal.
        int sx = StageX(w), foot = h - 92;
        for (int r = 46; r > 0; r -= 3) ui.AlphaDisc(sx, foot - 50, r, TealDark, 0.05f);
        Pedestal(ui, sx, foot, 40, 10);
        float bob = MathF.Sin(_menuTime * 2.2f);
        for (int i = 0; i < 6; i++)
        {
            float a = _menuTime * 0.7f + i * MathF.Tau / 6;
            float px = sx + MathF.Cos(a) * 44, py = foot - 6 + MathF.Sin(a) * 9 - (i % 3) * 18 - (_menuTime * 9 + i * 13) % 60;
            ui.Blend(px, py, Teal, 0.55f);
        }
        preview.DrawHero(ui, selected, _menuTime, sx, foot + 2 - (bob > 0.6f ? 1 : 0), 5);
        var (bx, by) = StartButton(w, h);
        bool startHover = MenuStartAt(renderer, mouse);
        Frame(ui, bx, by, StartW, StartH, Teal, startHover ? PanelLit : Panel);
        PixelFont.DrawCentered(ui, Localizer.Get("menu.start"), bx + StartW / 2, by + 5, startHover ? White : Teal, Ink);

        // Info panel: name, table stats, passive and growth.
        var c = content.Characters[selected];
        var s = c.Stats;
        int px0 = PanelX(w), py0 = RosterY, ph = h - py0 - 74;
        Frame(ui, px0, py0, PanelW, ph, Teal, Panel);
        int tx = px0 + 10, ty = py0 + 10;
        PixelFont.Draw(ui, CharacterName(c), tx, ty, White, Ink, 2);
        for (int x = tx; x < px0 + PanelW - 10; x++) ui.Set(x, ty + 18, TealDark);
        int ly = ty + 26;
        PixelFont.Draw(ui, Localizer.Format("menu.health", F(s.MaxHp), F(s.MaxHpPerLevel)), tx, ly, White, Ink);
        PixelFont.Draw(ui, Localizer.Format("menu.damage", F(s.Damage), F(s.DamagePerLevel)), tx, ly + PixelFont.LineHeight + 2, White, Ink);
        PixelFont.Draw(ui, Localizer.Format("menu.speed", F(s.MoveSpeed), F(s.AttackSpeed)), tx, ly + 2 * (PixelFont.LineHeight + 2), White, Ink);
        PixelFont.Draw(ui, Localizer.Format("menu.resist", Pct(s.Armor), Pct(s.MagicResist)), tx, ly + 3 * (PixelFont.LineHeight + 2), White, Ink);

        int ix = px0 + 10, iy = ly + 4 * (PixelFont.LineHeight + 2) + 8, iw = PanelW - 20;
        PixelFont.Draw(ui, Localizer.Get("menu.passive"), ix, iy, Teal, Ink);
        iy += PixelFont.LineHeight + 1;
        foreach (var line in Wrap(Localizer.Content("characters", c.Id, "passive", c.Passive), iw / PixelFont.Advance))
        {
            PixelFont.Draw(ui, line, ix, iy, White, Ink);
            iy += PixelFont.LineHeight;
        }
        iy += 6;
        PixelFont.Draw(ui, Localizer.Get("menu.growth"), ix, iy, Teal, Ink);
        iy += PixelFont.LineHeight + 1;
        foreach (var line in Wrap(Localizer.Content("characters", c.Id, "growth", c.Growth), iw / PixelFont.Advance))
        {
            PixelFont.Draw(ui, line, ix, iy, Grey, Ink);
            iy += PixelFont.LineHeight;
        }

        string language = Localizer.Format("menu.language", Localizer.Get("language." + Localizer.Language));
        PixelFont.Draw(ui, language, w - 14 - PixelFont.Measure(language), 14, Grey, Ink);
        PixelFont.DrawCentered(ui, Localizer.Get("menu.help"), w / 2, h - 16, Teal, Ink);
    }

    /// <summary>Slow diagonal light bands and a darker rim behind the menu panels (cosmetic, menu clock only).</summary>
    private void DrawMenuBackdrop(PixelCanvas ui, int w, int h)
    {
        float shift = _menuTime * 6;
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                float band = MathF.Sin((x + y * 0.6f + shift) * 0.035f);
                if (band > 0.82f) ui.Blend(x, y, TealDark, 0.08f);
            }
        for (int y = 0; y < h; y++)
        {
            float k = MathF.Abs(y - h / 2f) / (h / 2f);
            if (k < 0.75f) continue;
            for (int x = 0; x < w; x++) ui.Blend(x, y, 0xFF0A0508u, (k - 0.75f) * 1.4f);
        }
    }

    private static void Pedestal(PixelCanvas ui, int cx, int cy, int rx, int ry)
    {
        for (int y = -ry; y <= ry + 3; y++)
            for (int x = -rx; x <= rx; x++)
            {
                float top = (x * x) / (float)(rx * rx) + (y * y) / (float)(ry * ry);
                float side = (x * x) / (float)(rx * rx) + ((y - 3) * (y - 3)) / (float)(ry * ry);
                if (top <= 1) ui.Set(cx + x, cy + y, top > 0.8f ? Teal : top > 0.55f ? PanelLit : PanelEdge);
                else if (side <= 1 && y > 0) ui.Set(cx + x, cy + y, Ink);
            }
    }

    /// <summary>Panel with a 1px border and darker inner corner pixels (bevelled look).</summary>
    private static void Frame(PixelCanvas ui, int x, int y, int fw, int fh, uint border, uint fill)
    {
        for (int yy = 0; yy < fh; yy++)
            for (int xx = 0; xx < fw; xx++)
            {
                bool edge = xx == 0 || yy == 0 || xx == fw - 1 || yy == fh - 1;
                bool corner = (xx == 0 || xx == fw - 1) && (yy == 0 || yy == fh - 1);
                if (corner) continue;
                ui.Set(x + xx, y + yy, edge ? border : yy == 1 ? VfxMath.Mix(fill, 0xFFFFFFFFu, 0.08f) : fill);
            }
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
