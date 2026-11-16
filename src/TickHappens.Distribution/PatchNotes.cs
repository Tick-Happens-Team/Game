using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickHappens.Distribution;

/// <summary>
/// Patch notes for one release in one language, structured like a balance patch: general changes, then sections for
/// heroes, items and enemies, each with an icon, optional groups (abilities, growth) of named entries, and changes
/// with optional detail lines. Signed as part of <see cref="ChannelManifest"/>.
/// </summary>
public sealed class PatchNotes
{
    public static readonly string[] Kinds = ["hero", "item", "enemy", "other"];
    public static readonly string[] ChangeTags = ["buff", "nerf", "rework", "new", "fix"];
    private const int MaxText = 300, MaxLines = 400;

    [JsonPropertyName("general")] public List<PatchChange> General { get; set; } = [];
    [JsonPropertyName("sections")] public List<PatchSection> Sections { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    /// <summary>Hand-written files reject unknown fields so typos fail the release instead of vanishing.</summary>
    private static readonly JsonSerializerOptions Strict = new(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    /// <summary>Parses and validates a hand-written notes file.</summary>
    public static PatchNotes FromJson(string json)
    {
        var notes = JsonSerializer.Deserialize<PatchNotes>(json, Strict) ?? throw new InvalidDataException("Empty patch notes.");
        notes.Validate();
        return notes;
    }

    [JsonIgnore] public bool IsEmpty => General.Count == 0 && Sections.Count == 0;

    /// <summary>Rejects unknown kinds and change tags, empty or multi-line text, and oversized notes before signing.</summary>
    public void Validate()
    {
        int lines = 0;
        void Text(string? text, string what)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxText || text.Any(char.IsControl))
                throw new InvalidDataException($"Patch notes: {what} needs one line of 1-{MaxText} characters.");
            if (++lines > MaxLines) throw new InvalidDataException($"Patch notes exceed {MaxLines} lines.");
        }
        void Changes(List<PatchChange> changes)
        {
            foreach (var change in changes)
            {
                Text(change.Text, "a change");
                if (change.Change is { } tag && !ChangeTags.Contains(tag))
                    throw new InvalidDataException($"Patch notes: change must be one of {string.Join(", ", ChangeTags)}, not '{tag}'.");
                foreach (string detail in change.Details) Text(detail, "a detail");
            }
        }
        Changes(General);
        foreach (var section in Sections)
        {
            if (!Kinds.Contains(section.Kind))
                throw new InvalidDataException($"Patch notes: section kind must be one of {string.Join(", ", Kinds)}, not '{section.Kind}'.");
            if (section.Id is null && section.Title is null) throw new InvalidDataException("Patch notes: a section needs an id or a title.");
            if (section.Title is not null) Text(section.Title, "a section title");
            Changes(section.Changes);
            foreach (var group in section.Groups)
            {
                Text(group.Title, "a group title");
                foreach (var entry in group.Entries)
                {
                    Text(entry.Name, "an entry name");
                    Changes(entry.Changes);
                }
            }
        }
    }

    /// <summary>Markdown for the GitHub release page. Sections without a title use <paramref name="name"/>, or their id.</summary>
    public string ToMarkdown(Func<PatchSection, string?>? name = null)
    {
        var md = new StringBuilder();
        void Changes(List<PatchChange> changes, string indent)
        {
            foreach (var change in changes)
            {
                md.Append(indent).Append("- ").Append(Tag(change.Change)).AppendLine(change.Text);
                foreach (string detail in change.Details) md.Append(indent).Append("  - ").AppendLine(detail);
            }
        }
        if (General.Count != 0)
        {
            Changes(General, "");
            md.AppendLine();
        }
        foreach (var section in Sections)
        {
            md.Append("### ").AppendLine(section.Title ?? name?.Invoke(section) ?? section.Id).AppendLine();
            Changes(section.Changes, "");
            foreach (var group in section.Groups)
            {
                md.AppendLine().Append("**").Append(group.Title).AppendLine("**").AppendLine();
                foreach (var entry in group.Entries)
                {
                    md.Append("- **").Append(entry.Name).AppendLine("**");
                    Changes(entry.Changes, "  ");
                }
            }
            md.AppendLine();
        }
        return md.ToString().TrimEnd() + "\n";
    }

    private static string Tag(string? change) => change switch
    {
        "buff" => "▲ ", "nerf" => "▼ ", "rework" => "◆ ", "new" => "★ ", "fix" => "✔ ", _ => "",
    };
}

/// <summary>A hero, item, enemy, or free-form section. The id selects the localized name and the default icon.</summary>
public sealed class PatchSection
{
    /// <summary>"hero" | "item" | "enemy" | "other".</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "other";
    /// <summary>Content id from characters.json, items.json or enemies.json.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    /// <summary>Overrides the localized content name.</summary>
    [JsonPropertyName("title")] public string? Title { get; set; }
    /// <summary>Atlas sprite id; heroes and enemies default to their content sprite.</summary>
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("changes")] public List<PatchChange> Changes { get; set; } = [];
    [JsonPropertyName("groups")] public List<PatchGroup> Groups { get; set; } = [];
}

/// <summary>A titled group inside a section, such as abilities or growth.</summary>
public sealed class PatchGroup
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("entries")] public List<PatchEntry> Entries { get; set; } = [];
}

/// <summary>A named part of a section (an ability, a passive, a weapon) with its own optional icon.</summary>
public sealed class PatchEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("changes")] public List<PatchChange> Changes { get; set; } = [];
}

/// <summary>One change, optionally tagged buff, nerf, rework, new or fix, with indented detail lines.</summary>
public sealed class PatchChange
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("change")] public string? Change { get; set; }
    [JsonPropertyName("details")] public List<string> Details { get; set; } = [];
}
