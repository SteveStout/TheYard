using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// The picker's two files on the share. In: the inbox pass's leads files (nightly/leads-&lt;date&gt;.md), whose own
/// tables carry a Filter column, read for the rows that passed, each dated by its alert's section. Out: the night's
/// candidates file (nightly/jobs-candidates-&lt;date&gt;.tsv), one row a candidate in the columns the jobs lane reads, its
/// supply line first, and every role a gate failed under the line "# FAILED" with that gate, so nothing is dropped
/// unseen.
/// </summary>
public static partial class JobsFiles
{
    /// <summary>The event that starts the jobs lane in nightly.yml.</summary>
    public const string Event = "jobs";

    /// <summary>The candidates file for a Central day.</summary>
    public static string Candidates(DateOnly day) => $"nightly/jobs-candidates-{day:yyyy-MM-dd}.tsv";

    /// <summary>The inbox pass's leads file for a Central day.</summary>
    public static string Leads(DateOnly day) => $"nightly/leads-{day:yyyy-MM-dd}.md";

    /// <summary>The columns, in order.</summary>
    public static readonly IReadOnlyList<string> Columns = ["Source", "Company", "Title", "Level", "Url", "BoardKind", "Posted", "DaysOld", "Band", "BandFlag", "Remote", "Confirmed", "Stack", "TrackerNote", "WhyPassed"];

    /// <summary>The alert rows that passed the inbox pass's filter, as candidates dated by their alert, one a link.</summary>
    public static IReadOnlyList<Candidate> AlertRows(string text)
    {
        var rows = new List<Candidate>();
        string board = "alert";
        DateOnly? seen = null;
        foreach (string raw in (text ?? string.Empty).Split('\n'))
        {
            string line = raw.Trim();
            if (Section().Match(line) is { Success: true } section)
            {
                board = section.Groups[1].Value.Trim();
                seen = DateOnly.ParseExact(section.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                continue;
            }
            string[] cells = line.Trim('|').Split(" | ").Select(c => c.Trim()).ToArray();
            if (cells.Length != 6 || cells[5] != "pass" || !cells[3].StartsWith("https://", StringComparison.Ordinal))
            {
                continue;
            }
            int band = cells[2].IndexOf("; band ", StringComparison.Ordinal);
            string where = band < 0 ? cells[2] : cells[2][..band];
            var figures = band < 0 ? [] : Thousands().Matches(cells[2][band..]).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 1000).ToList();
            rows.Add(new Candidate("alert:" + board, cells[0], cells[1], cells[3], board, seen, where, "Unknown", figures.Count == 0 ? null : figures.Max(), "alert", "")
            {
                BandMin = figures.Count == 0 ? null : figures.Min(),
            });
        }
        return [.. rows.DistinctBy(r => r.Url)];
    }

    /// <summary>The candidates file: the supply line, the header, the passed rows in order, then the failed ones under "# FAILED".</summary>
    public static string Write(string supply, IReadOnlyList<Verdict> passed, IReadOnlyList<Verdict> failed)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(Flat(supply)).Append('\n');
        text.AppendJoin('\t', Columns).Append('\n');
        foreach (var verdict in passed)
        {
            text.AppendJoin('\t', Cells(verdict)).Append('\n');
        }
        text.Append("# FAILED\n");
        text.AppendJoin('\t', [.. Columns, "Failed"]).Append('\n');
        foreach (var verdict in failed)
        {
            text.AppendJoin('\t', [.. Cells(verdict), Flat(verdict.Failed ?? "")]).Append('\n');
        }
        return text.ToString();
    }

    private static IEnumerable<string> Cells(Verdict v)
    {
        var c = v.Candidate;
        string band = c.BandMax is { } max ? c.BandMin is { } min && min != max ? $"{Dollars(min)}-{Dollars(max)}" : Dollars(max) : "";
        return new[]
        {
            c.Source, c.Company, c.Title, c.Level, c.Url, c.BoardKind, c.Posted?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            v.DaysOld.ToString(CultureInfo.InvariantCulture), band, v.BandFlag, v.Remote, c.Confirmed, v.Stack, v.TrackerNote, v.WhyPassed,
        }.Select(Flat);
    }

    private static string Dollars(int value) => "$" + value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Flat(string text) => text.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    [GeneratedRegex(@"^## \S+ \| ([^,|]+)[^|]*\| (\d{4}-\d{2}-\d{2}) \d{2}:\d{2} Central \|")]
    private static partial Regex Section();

    [GeneratedRegex(@"\$(\d{2,3})K", RegexOptions.IgnoreCase)]
    private static partial Regex Thousands();
}
