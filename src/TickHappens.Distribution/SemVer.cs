using System.Text.RegularExpressions;

namespace TickHappens.Distribution;

/// <summary>Semantic version (MAJOR.MINOR.PATCH[-pre]); build metadata is ignored for ordering.</summary>
public readonly partial record struct SemVer(int Major, int Minor, int Patch, string Pre = "") : IComparable<SemVer>
{
    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();

    public static SemVer Parse(string s) =>
        TryParse(s, out var v) ? v : throw new FormatException($"invalid semver '{s}'");

    public static bool TryParse(string s, out SemVer v)
    {
        var m = Pattern().Match(s.Trim().TrimStart('v'));
        if (!m.Success) { v = default; return false; }
        v = new SemVer(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value);
        return true;
    }

    public int CompareTo(SemVer o)
    {
        int c = Major.CompareTo(o.Major);
        if (c == 0) c = Minor.CompareTo(o.Minor);
        if (c == 0) c = Patch.CompareTo(o.Patch);
        if (c != 0) return c;
        // A pre-release sorts before the release; pre-release identifiers compare per SemVer 2.0 rules.
        if (Pre.Length == 0 || o.Pre.Length == 0) return o.Pre.Length.CompareTo(Pre.Length) switch { > 0 => 1, < 0 => -1, _ => 0 } * (Pre.Length == 0 ? 1 : 1);
        var a = Pre.Split('.');
        var b = o.Pre.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool na = int.TryParse(a[i], out int ia), nb = int.TryParse(b[i], out int ib);
            int r = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (r != 0) return r;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;

    public override string ToString() => Pre.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Pre}";
}

/// <summary>
/// Version range: space-separated comparators that must all hold, e.g. "&gt;=0.4.0 &lt;0.5.0", "=1.2.3", "*".
/// "||" separates alternatives. Bare "0.4" means "&gt;=0.4.0 &lt;0.5.0" (same minor).
/// </summary>
public sealed class VersionRange
{
    private readonly List<List<(string Op, SemVer V)>> _alternatives = [];
    public string Text { get; }

    public VersionRange(string text)
    {
        Text = text.Trim();
        foreach (var alt in Text.Split("||"))
        {
            var set = new List<(string, SemVer)>();
            foreach (var tok in alt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok == "*") continue;
                string op = tok.StartsWith(">=") || tok.StartsWith("<=") ? tok[..2] : tok[0] is '>' or '<' or '=' ? tok[..1] : "";
                string ver = tok[op.Length..];
                if (op == "" && ver.Count(c => c == '.') == 1)
                {
                    var b = SemVer.Parse(ver + ".0");
                    set.Add((">=", b));
                    set.Add(("<", new SemVer(b.Major, b.Minor + 1, 0)));
                    continue;
                }
                set.Add((op == "" ? "=" : op, SemVer.Parse(ver)));
            }
            _alternatives.Add(set);
        }
    }

    public bool Contains(SemVer v) => _alternatives.Any(set => set.All(c => c.Op switch
    {
        ">=" => v >= c.V,
        "<=" => v <= c.V,
        ">" => v > c.V,
        "<" => v < c.V,
        _ => v.CompareTo(c.V) == 0,
    }));

    public override string ToString() => Text;
}
