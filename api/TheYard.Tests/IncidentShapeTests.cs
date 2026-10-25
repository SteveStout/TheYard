using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The shape every incident keeps (ADR: A red that lasts an hour gets a postmortem). An incident is
/// read beside the others, so each one has the same eight sections in the same order; its timeline
/// names where every row was read; every version it says fixed something is a version the changelog
/// shipped; every follow-up says its state; and the index lists it.
/// </summary>
public sealed partial class IncidentShapeTests
{
    // #region incident-shape
    /// <summary>The eight sections after "In plain words", in the order every incident keeps them.</summary>
    private static readonly string[] Sections =
    [
        "Summary", "Timeline", "Impact", "Root cause", "Detection gap", "What changed", "What stays true", "Follow-ups",
    ];

    private static string Folder => Path.Combine(Repo.Root(), "docs", "incidents");

    private static IEnumerable<(string Name, string[] Lines)> Incidents() =>
        Directory.EnumerateFiles(Folder, "INC-*.md")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllLines(path)));

    [GeneratedRegex(@"^INC-\d{3}-[a-z0-9-]+\.md$")]
    private static partial Regex FileName();

    [GeneratedRegex(@"\b1\.0\.\d+\.\d+\b")]
    private static partial Regex Version();
    // #endregion incident-shape

    [Fact]
    public void There_is_at_least_one_incident_and_every_file_is_named_for_its_number()
    {
        var names = Incidents().Select(incident => incident.Name).ToList();
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.Matches(FileName(), name));
        Assert.All(Directory.EnumerateFiles(Folder, "*.md").Select(Path.GetFileName),
            name => Assert.True(name == "INCIDENTS.md" || FileName().IsMatch(name!), $"{name} is neither the index nor an incident"));
    }

    [Fact]
    public void Every_incident_has_its_title_its_status_and_the_eight_sections_in_order()
    {
        var wrong = new List<string>();
        foreach ((string name, string[] lines) in Incidents())
        {
            string id = name[..7];
            if (!lines[0].StartsWith($"# {id}: ", StringComparison.Ordinal))
            {
                wrong.Add($"{name} does not open '# {id}: '");
            }

            int status = Array.FindIndex(lines, line => line.StartsWith("Status: ", StringComparison.Ordinal));
            int first = Array.FindIndex(lines, line => line.StartsWith("## ", StringComparison.Ordinal));
            if (status < 0 || (first >= 0 && status > first))
            {
                wrong.Add($"{name} has no status line above its first section");
            }

            string[] headings = [.. lines.Where(line => line.StartsWith("## ", StringComparison.Ordinal)).Select(line => line[3..].Trim())];
            string[] expected = ["In plain words", .. Sections];
            if (!headings.SequenceEqual(expected))
            {
                wrong.Add($"{name} has the sections [{string.Join(", ", headings)}]; an incident has [{string.Join(", ", expected)}]");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_timeline_row_names_its_source()
    {
        var wrong = new List<string>();
        foreach ((string name, string[] lines) in Incidents())
        {
            List<string[]> rows = Table(lines, "Timeline", out string[] header);
            int source = Array.FindIndex(header, cell => cell.Equals("Source", StringComparison.OrdinalIgnoreCase));
            if (source < 0)
            {
                wrong.Add($"{name}'s Timeline is not a table with a Source column");
                continue;
            }

            if (rows.Count == 0)
            {
                wrong.Add($"{name}'s Timeline has no rows");
            }

            wrong.AddRange(rows.Where(row => row.Length <= source || row[source].Length == 0)
                .Select(row => $"{name}'s Timeline row '{row[0]}' names no source"));
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_version_in_what_changed_is_a_line_of_the_changelog()
    {
        string changelog = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "CHANGELOG.md"));
        var wrong = new List<string>();
        foreach ((string name, string[] lines) in Incidents())
        {
            List<string[]> rows = Table(lines, "What changed", out string[] header);
            int column = Array.FindIndex(header, cell => cell.Equals("Version", StringComparison.OrdinalIgnoreCase));
            if (column < 0 || rows.Count == 0)
            {
                wrong.Add($"{name}'s What changed is not a table with a Version column and a row");
                continue;
            }

            foreach (string[] row in rows)
            {
                string cell = row.Length > column ? row[column] : string.Empty;
                var versions = Version().Matches(cell).Select(match => match.Value).ToList();
                if (versions.Count == 0 && cell != "no version")
                {
                    wrong.Add($"{name}'s What changed row '{row[0]}' names neither a version nor 'no version'");
                }

                wrong.AddRange(versions.Where(version => !changelog.Contains($"- **{version}** (", StringComparison.Ordinal))
                    .Select(version => $"{name} names {version}, which the changelog never shipped"));
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_follow_up_says_its_state_and_the_index_lists_every_incident()
    {
        string index = File.ReadAllText(Path.Combine(Folder, "INCIDENTS.md"));
        var wrong = new List<string>();
        foreach ((string name, string[] lines) in Incidents())
        {
            List<string[]> rows = Table(lines, "Follow-ups", out string[] header);
            int state = Array.FindIndex(header, cell => cell.Equals("State", StringComparison.OrdinalIgnoreCase));
            if (state < 0)
            {
                wrong.Add($"{name}'s Follow-ups is not a table with a State column");
            }
            else
            {
                wrong.AddRange(rows.Where(row => row.Length <= state || row[state].Length == 0)
                    .Select(row => $"{name}'s follow-up '{row[0]}' says no state"));
            }

            string slug = name[..^".md".Length].ToLowerInvariant();
            if (!index.Contains($"| [{name[..7]}](https://theyard.stevenstout.biz/?doc={slug})", StringComparison.Ordinal))
            {
                wrong.Add($"INCIDENTS.md does not list {name[..7]} with a link to ?doc={slug}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>The first table under a section: its header cells, and each row's cells, trimmed.</summary>
    private static List<string[]> Table(string[] lines, string section, out string[] header)
    {
        int start = Array.FindIndex(lines, line => line.Trim() == "## " + section);
        string[] table = start < 0
            ? []
            : [.. lines.Skip(start + 1)
                .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
                .SkipWhile(line => !line.TrimStart().StartsWith('|'))
                .TakeWhile(line => line.TrimStart().StartsWith('|'))];
        header = table.Length > 0 ? Cells(table[0]) : [];
        return [.. table.Skip(2).Select(Cells)];
    }

    private static string[] Cells(string row) =>
        [.. row.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];
}
