using System.Reflection;
using System.Text.Json;
using StbImageSharp;
using TickHappens.Distribution;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Vfx;
using TickHappens.Localization;
using static TickHappens.Launcher.Paint;

namespace TickHappens.Launcher;

/// <summary>
/// The patch notes page, laid out like a balance patch: general changes, then a section per hero, item or enemy with
/// its sprite in a frame and its localized name, groups such as abilities, named entries with small icons, and
/// change lines marked buff ▲, nerf ▼, rework ◆, new ★ or fix, with indented details. The notes come from the signed
/// channel manifest of the installed release; icons come from that release's atlas, or the launcher's embedded one.
/// </summary>
internal sealed class NotesPage
{
    private enum RowKind { Section, Group, Entry, Change, Detail, Gap }

    private readonly record struct Row(RowKind Kind, int Height, string Text, string? Tag = null,
        (RgbaImage Image, AtlasSprite Sprite)? Icon = null, string? Caption = null, int Indent = 0, bool First = true);

    private const int Top = 64, SectionIcon = 24, EntryIcon = 16;

    private readonly List<Row> _rows = [];
    private (long Sequence, string Language, int Width, string? Folder) _layoutKey;
    private Atlas? _atlas;
    private string? _atlasFolder = "\0";
    private int _scroll;

    private sealed record Atlas(RgbaImage Image, AtlasData Data);

    public int Scroll(int delta, int bottom) => _scroll = Math.Clamp(_scroll + delta, 0, MaxScroll(bottom));

    public void Reset() => _scroll = 0;

    public void Draw(PixelCanvas c, int x0, int w, int bottom, InstalledRelease? installed, string? gameFolder)
    {
        Label(c, Localizer.Get("launcher.page.notes"), x0, 16, Ink, 2);
        string heading = installed is { } v
            ? Localizer.Format("launcher.notesFor", v.Game, v.Release.Published.ToLocalTime().DateTime)
            : Localizer.Get("launcher.notInstalled");
        Label(c, heading, x0, 38, Muted);
        Bevel(c, x0, Top - 6, w, bottom - Top, White, White, PanelDark);

        var notes = installed?.Release.NotesFor(Localizer.Language);
        if (notes is null || notes.IsEmpty)
        {
            Centered(c, Localizer.Get("launcher.noNotes"), x0 + w / 2, Top + 60, Muted);
            return;
        }
        var key = (installed!.Sequence, Localizer.Language, w, gameFolder);
        if (key != _layoutKey)
        {
            _layoutKey = key;
            LoadAtlas(gameFolder);
            Layout(notes, w - 44);
            _scroll = Math.Min(_scroll, MaxScroll(bottom));
        }

        int y = Top, left = x0 + 10;
        for (int i = _scroll; i < _rows.Count && y + _rows[i].Height <= bottom - 8; i++)
        {
            DrawRow(c, _rows[i], left, y, w - 28);
            y += _rows[i].Height;
        }
        if (MaxScroll(bottom) > 0)
        {
            int track = bottom - Top - 16, thumb = Math.Max(10, track * Math.Max(1, _rows.Count - MaxScroll(bottom)) / _rows.Count);
            int offset = (track - thumb) * _scroll / MaxScroll(bottom);
            Fill(c, x0 + w - 8, Top - 2 + offset, 3, thumb, PanelDark);
        }
    }

    /// <summary>The first row from which the rest of the notes fit, so scrolling stops at the end.</summary>
    private int MaxScroll(int bottom)
    {
        int space = bottom - 8 - Top, used = 0;
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            used += _rows[i].Height;
            if (used > space) return i + 1;
        }
        return 0;
    }

    private void Layout(PatchNotes notes, int width)
    {
        _rows.Clear();
        AddChanges(notes.General, width, 0);
        foreach (var section in notes.Sections)
        {
            if (_rows.Count != 0) _rows.Add(new(RowKind.Gap, 8, ""));
            string type = section.Kind switch { "hero" => "characters", "item" => "items", "enemy" => "enemies", _ => "" };
            string title = section.Title ?? Localizer.Content(type, section.Id!, "name", section.Id!);
            string? kind = section.Kind == "other" ? null : Localizer.Get("launcher.notes.kind." + section.Kind);
            _rows.Add(new(RowKind.Section, SectionIcon + 12, title, Icon: Sprite(section.Icon ?? DefaultIcon(section)), Caption: kind));
            AddChanges(section.Changes, width, 0);
            foreach (var group in section.Groups)
            {
                _rows.Add(new(RowKind.Group, 15, group.Title));
                foreach (var entry in group.Entries)
                {
                    _rows.Add(new(RowKind.Entry, EntryIcon + 4, entry.Name, Icon: Sprite(entry.Icon)));
                    AddChanges(entry.Changes, width, EntryIcon + 6);
                }
            }
        }
    }

    private void AddChanges(List<PatchChange> changes, int width, int indent)
    {
        foreach (var change in changes)
        {
            var lines = Wrap(change.Text, width - indent - 12);
            for (int i = 0; i < lines.Count; i++)
                _rows.Add(new(RowKind.Change, 11, lines[i], change.Change, Indent: indent, First: i == 0));
            foreach (string detail in change.Details)
            {
                var detailLines = Wrap(detail, width - indent - 26);
                for (int i = 0; i < detailLines.Count; i++)
                    _rows.Add(new(RowKind.Detail, 11, detailLines[i], Indent: indent, First: i == 0));
            }
        }
    }

    private static void DrawRow(PixelCanvas c, Row row, int x, int y, int w)
    {
        switch (row.Kind)
        {
            case RowKind.Section:
                Fill(c, x - 4, y - 2, w + 4, 1, PanelDark);
                Bevel(c, x, y + 2, SectionIcon + 4, SectionIcon + 4, Panel, White, PanelDark);
                DrawIcon(c, row.Icon, x + 2, y + 4, SectionIcon);
                Label(c, row.Text, x + SectionIcon + 12, y + 6, Ink, 2);
                if (row.Caption is { } kind) Label(c, kind, x + SectionIcon + 12, y + 23, Muted);
                break;
            case RowKind.Group:
                Label(c, row.Text, x + 2, y + 4, Gold);
                break;
            case RowKind.Entry:
                if (row.Icon is not null)
                {
                    Fill(c, x + 2, y + 1, EntryIcon + 2, EntryIcon + 2, Panel);
                    DrawIcon(c, row.Icon, x + 3, y + 2, EntryIcon);
                }
                Label(c, row.Text, x + EntryIcon + 8, y + 6, Ink);
                break;
            case RowKind.Change:
            {
                int lx = x + row.Indent;
                if (row.First) Marker(c, row.Tag, lx + 4, y + 3);
                Label(c, row.Text, lx + 12, y + 1, Text);
                break;
            }
            case RowKind.Detail:
            {
                int lx = x + row.Indent + 12;
                if (row.First)
                {
                    Fill(c, lx + 2, y - 2, 1, 6, Muted);
                    Fill(c, lx + 2, y + 4, 6, 1, Muted);
                }
                Label(c, row.Text, lx + 12, y + 1, Muted);
                break;
            }
        }
    }

    /// <summary>A 5×5 change marker: green up triangle, red down triangle, teal diamond, gold star, or a plain dot.</summary>
    private static void Marker(PixelCanvas c, string? tag, int x, int y)
    {
        switch (tag)
        {
            case "buff":
                for (int r = 0; r < 3; r++) Fill(c, x + 2 - r, y + 1 + r, 1 + r * 2, 1, GreenDark);
                break;
            case "nerf":
                for (int r = 0; r < 3; r++) Fill(c, x + r, y + 1 + r, 5 - r * 2, 1, Danger);
                break;
            case "rework":
                Fill(c, x + 2, y, 1, 5, Accent);
                Fill(c, x + 1, y + 1, 3, 3, Accent);
                Fill(c, x, y + 2, 5, 1, Accent);
                break;
            case "new":
                Fill(c, x + 2, y, 1, 5, Gold);
                Fill(c, x, y + 2, 5, 1, Gold);
                Fill(c, x + 1, y + 1, 3, 3, Gold);
                c.Set(x + 2, y + 2, White);
                break;
            case "fix":
                Fill(c, x, y + 2, 1, 1, Accent);
                Fill(c, x + 1, y + 3, 1, 1, Accent);
                Fill(c, x + 2, y + 2, 1, 1, Accent);
                Fill(c, x + 3, y + 1, 1, 1, Accent);
                Fill(c, x + 4, y, 1, 1, Accent);
                break;
            default:
                Fill(c, x + 1, y + 1, 3, 3, Muted);
                break;
        }
    }

    /// <summary>Draws the sprite's first idle frame scaled by a whole number to fit the box and centred in it.</summary>
    private static void DrawIcon(PixelCanvas c, (RgbaImage Image, AtlasSprite Sprite)? icon, int x, int y, int box)
    {
        if (icon is not { } found) return;
        var (image, s) = found;
        int frame = s.Anims.TryGetValue("idle", out var idle) && idle.Frames.Count > 0 ? idle.Frames[0] : 0;
        if (frame >= s.Frames.Count) return;
        int fx = s.Frames[frame][0], fy = s.Frames[frame][1];
        int scale = Math.Max(1, box / Math.Max(s.W, s.H));
        int w = Math.Min(box, s.W * scale), h = Math.Min(box, s.H * scale);
        int ox = x + (box - w) / 2, oy = y + (box - h) / 2;
        // Larger sprites are cropped around their centre.
        int sx0 = Math.Max(0, (s.W * scale - box) / 2), sy0 = Math.Max(0, (s.H * scale - box) / 2);
        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                uint color = image[fx + (sx0 + px) / scale, fy + (sy0 + py) / scale];
                if (RgbaImage.A(color) != 0) c.Set(ox + px, oy + py, color);
            }
    }

    private (RgbaImage, AtlasSprite)? Sprite(string? id) =>
        id is not null && _atlas is { } atlas && atlas.Data.Sprites.TryGetValue(id, out var sprite) ? (atlas.Image, sprite) : null;

    private static string? DefaultIcon(PatchSection section) => section.Kind switch
    {
        "hero" => ContentSprite("characters.json", section.Id),
        "enemy" => ContentSprite("enemies.json", section.Id),
        _ => null,
    };

    private static string? ContentSprite(string resource, string? id)
    {
        if (id is null) return null;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null) return null;
        using var document = JsonDocument.Parse(stream);
        foreach (var entry in document.RootElement.EnumerateArray())
            if (entry.TryGetProperty("id", out var value) && value.GetString() == id &&
                entry.TryGetProperty("sprite", out var sprite))
                return sprite.GetString();
        return null;
    }

    /// <summary>Prefers the installed release's atlas so new heroes have icons; falls back to the embedded atlas.</summary>
    private void LoadAtlas(string? gameFolder)
    {
        if (_atlasFolder == gameFolder) return;
        _atlasFolder = gameFolder;
        _atlas = null;
        try
        {
            if (gameFolder is not null && File.Exists(Path.Combine(gameFolder, "assets/atlas/main.png")))
            {
                using var png = File.OpenRead(Path.Combine(gameFolder, "assets/atlas/main.png"));
                _atlas = Read(png, AtlasData.Load(Path.Combine(gameFolder, "assets/atlas/main.json")));
                return;
            }
            var assembly = Assembly.GetExecutingAssembly();
            using var embedded = assembly.GetManifestResourceStream("atlas.png");
            using var json = assembly.GetManifestResourceStream("atlas.json");
            if (embedded is not null && json is not null)
                _atlas = Read(embedded, JsonSerializer.Deserialize<AtlasData>(json, AtlasData.Json) ?? new AtlasData());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException) { }
    }

    private static Atlas Read(Stream png, AtlasData data)
    {
        var image = ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);
        var pixels = new uint[image.Width * image.Height];
        Buffer.BlockCopy(image.Data, 0, pixels, 0, image.Data.Length);
        return new(new RgbaImage(image.Width, image.Height, pixels), data);
    }
}
