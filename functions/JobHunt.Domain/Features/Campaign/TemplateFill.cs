using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>A filled template: the text, or nothing and the names of the blanks that stopped it.</summary>
public sealed record Filled(string? Text, IReadOnlyList<string> Blanks)
{
    /// <summary>Whether every required variable had a value.</summary>
    public bool Complete => Text is not null;
}

/// <summary>
/// Code fills the template and adds nothing (Steve, 12:22): every {Variable} takes its value from the lead's row.
/// A required variable left blank fills nothing and names the blank (the review, F12), so no email goes out with
/// a hole in it. An optional line (8.2 item 2) is a line whose variables are all optional: when any of them is
/// blank the whole line goes, and the blank line under it with it, so no gap is left where it stood.
/// </summary>
public static partial class TemplateFill
{
    /// <summary>The variables a template names, in order, each once.</summary>
    public static IReadOnlyList<string> Variables(string template) =>
        [.. Variable().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>Fill a template from the row's values.</summary>
    public static Filled Fill(string template, IReadOnlyDictionary<string, string?> values, IReadOnlySet<string> optional)
    {
        string[] lines = template.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>();
        var blanks = new List<string>();
        bool dropped = false;
        foreach (string line in lines)
        {
            var names = Variable().Matches(line).Select(m => m.Groups[1].Value).ToList();
            bool isOptional = names.Count > 0 && names.All(optional.Contains);
            if (isOptional && names.Any(name => Blank(values, name)))
            {
                dropped = true;
                continue;
            }
            if (dropped && line.Trim().Length == 0 && (kept.Count == 0 || kept[^1].Trim().Length == 0))
            {
                dropped = false;
                continue;
            }
            dropped = false;
            blanks.AddRange(names.Where(name => Blank(values, name) && !blanks.Contains(name)));
            kept.Add(Variable().Replace(line, m => values.TryGetValue(m.Groups[1].Value, out string? v) ? v ?? "" : ""));
        }
        return blanks.Count > 0 ? new Filled(null, blanks) : new Filled(string.Join('\n', kept), []);
    }

    private static bool Blank(IReadOnlyDictionary<string, string?> values, string name) =>
        !values.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value);

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex Variable();
}
