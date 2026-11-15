using System.Globalization;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Platform;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Sim;
using TickHappens.Localization;

namespace TickHappens.Game;

internal sealed class ItemPanel(Func<int, string> playerName)
{
    private const uint Face = 0xEB2C1D23u, Edge = 0xFF594254u, Ink = 0xFF22141Au;
    private const uint Accent = 0xFFDEF08Fu, Text = 0xFFEAECF4u;
    private const int ScrollRows = 5;
    private readonly record struct Line(string Text, int Start, int Length, uint Color, int Stacks);
    private readonly List<Line> _lines = [];
    private int _offset;

    public bool IsOpen { get; private set; }

    public void Read(GameWindow window, bool enabled)
    {
        IsOpen = enabled && window.IsDown(Key.Tab);
        if (!IsOpen) _offset = 0;
        else
        {
            if (window.WasPressed(Key.PageDown)) _offset += ScrollRows;
            if (window.WasPressed(Key.PageUp)) _offset = Math.Max(0, _offset - ScrollRows);
        }
    }

    public void Draw(PixelRenderer renderer, RunWorld world, bool online)
    {
        var ui = PixelCanvas.ColorOf(renderer.UiFrame);
        if (!IsOpen)
        {
            if (!online)
                PixelFont.DrawCentered(ui, Localizer.Get("hud.itemHint"), renderer.UiW / 2, renderer.UiH - 12, Text, Ink);
            return;
        }

        const int x = 6, y = Hud.RunHeaderBottom + 7, padding = 7, header = 18, footer = 15;
        int width = Math.Min(290, renderer.UiW / 2 - 12);
        int columns = (width - padding * 2) / PixelFont.Advance;
        int capacity = (renderer.UiH - y - header - footer - 6) / PixelFont.LineHeight;
        if (columns < 10 || capacity < 1) return;

        _lines.Clear();
        if (online)
        {
            foreach (var player in world.Players)
            {
                if (!player.Active) continue;
                if (_lines.Count > 0) AddLines("", columns, Text);
                AddLines(playerName(player.Id), columns, Accent);
                AddItems(world, player, columns);
            }
        }
        else AddItems(world, world.Player, columns);

        int visible = Math.Min(capacity, _lines.Count);
        int maxOffset = _lines.Count - visible;
        _offset = Math.Min(_offset, maxOffset);
        int height = header + visible * PixelFont.LineHeight + (maxOffset > 0 ? footer : 5);
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                ui.Set(x + col, y + row, row == 0 || row == height - 1 || col == 0 || col == width - 1 ? Edge : Face);

        PixelFont.Draw(ui, Localizer.Get(online ? "online.teamItems" : "hud.items"), x + padding, y + 5, Accent, Ink);
        ui.Line(x + padding, y + 14, x + width - padding - 1, y + 14, Edge);
        Span<char> countBuffer = stackalloc char[13];
        for (int row = 0; row < visible; row++)
        {
            var line = _lines[_offset + row];
            int top = y + header + row * PixelFont.LineHeight;
            PixelFont.Draw(ui, line.Text.AsSpan(line.Start, line.Length), x + padding, top, line.Color, Ink);
            if (line.Stacks <= 1) continue;
            countBuffer.TryWrite(CultureInfo.InvariantCulture, $"×{line.Stacks}", out int length);
            var count = countBuffer[..length];
            PixelFont.Draw(ui, count, x + width - padding - PixelFont.Measure(count), top, line.Color, Ink);
        }
        if (maxOffset > 0)
            PixelFont.Draw(ui, Localizer.Get("hud.scrollItems"), x + padding, y + height - 11, Accent, Ink);
    }

    private void AddItems(RunWorld world, PlayerState player, int columns)
    {
        bool found = false;
        for (int i = 0; i < player.ItemStacks.Length; i++)
        {
            int count = player.ItemStacks[i];
            if (count <= 0) continue;
            if (found) AddLines("", columns, Text);
            found = true;
            var item = world.Content.Items[i];
            AddLines(Hud.ItemName(item), columns, Hud.RarityColor(item.Rarity), count);
            AddLines(Localizer.Content("items", item.Id, "description", item.Description), columns, Text);
        }
        if (!found) AddLines(Localizer.Get("online.noItems"), columns, Text);
    }

    private void AddLines(string text, int columns, uint color, int stacks = 0)
    {
        int start = 0, limit = columns;
        if (stacks > 1)
        {
            Span<char> count = stackalloc char[11];
            stacks.TryFormat(count, out int digits, provider: CultureInfo.InvariantCulture);
            limit = Math.Max(1, columns - digits - 2);
        }
        do
        {
            var rest = text.AsSpan(start);
            int length = Math.Min(rest.Length, limit);
            int newline = rest[..length].IndexOf('\n');
            if (newline >= 0) length = newline;
            else if (length < rest.Length && !char.IsWhiteSpace(rest[length]))
            {
                int space = rest[..length].LastIndexOf(' ');
                if (space > 0) length = space;
            }
            _lines.Add(new Line(text, start, length, color, stacks));
            start += length;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            stacks = 0;
            limit = columns;
        }
        while (start < text.Length);
    }
}
