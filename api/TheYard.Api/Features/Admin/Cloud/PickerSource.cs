// The nightly picker's card's source (ADR-125, the addendum on the picker's card): the 2 o'clock hour's line the
// Nightly function writes ("jobs: <the candidates file>: <the supply line> | <the dispatch>") and the 1 AM BoardScan's
// line ("board scan: ..."), each joined on its operation to the request the Functions host wrote for that firing,
// read from the Log Analytics workspace with the site's identity, the way the run histories are. The share's own
// files (the pick log, the candidates file) are not read: the site holds no data role on the share.
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Admin.Cloud;

// #region picker-source
/// <summary>The picker's night and the board scan before it, read from the workspace an hour apart.</summary>
/// <param name="azure">Resource Manager, which forwards the query to the workspace.</param>
/// <param name="workspaceId">The workspace both Application Insights components send to.</param>
/// <param name="role">The Function's cloud role, its app name.</param>
public sealed partial class PickerSource(AzureMetrics azure, string workspaceId, string role)
{
    /// <summary>The readings a kept minute holds: the newest night's counts.</summary>
    public static IReadOnlyList<string> Readings { get; } = ["leads", "passed", "confirmed", "failed", "boards", "answered", "postings"];

    /// <summary>The newest picker line and BoardScan line, each with its firing's seconds and whether it succeeded.</summary>
    public const string Kusto = """
        let role = '__ROLE__';
        let since = ago(2d);
        let firings = AppRequests | where TimeGenerated > since and AppRoleName == role and Name in ('Nightly', 'BoardScan')
            | project OperationId, TookMs = DurationMs, Ok = tostring(Success) =~ 'true';
        AppTraces
        | where TimeGenerated > since and AppRoleName == role and (Message startswith 'jobs: ' or Message startswith 'board scan: ')
        | extend Kind = iff(Message startswith 'jobs: ', 'pick', 'scan')
        | summarize arg_max(TimeGenerated, Message, OperationId) by Kind
        | join kind=leftouter firings on OperationId
        | project Kind, At = TimeGenerated, Line = Message, TookMs, Ok
        """;

    /// <summary>The Function's name, which the card's line of the bill is matched on.</summary>
    public string Function => role;

    /// <summary>The query with the Function's role in place.</summary>
    public string Query => Kusto.Replace("__ROLE__", role.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);

    /// <summary>The newest night's counts as one minute, stamped at the read; nothing when the workspace is not read.</summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return [];
        }
        var facts = Facts(await azure.QueryAsync(workspaceId, Query, cancellation));
        var values = Readings.ToDictionary(reading => reading, reading => Number(facts.GetValueOrDefault(reading)), StringComparer.Ordinal);
        return values.Values.All(value => value is null) ? [] : [new ResourceMinute(to, values, version)];
    }

    /// <summary>The hour's facts: each line as written, when, its seconds, whether it succeeded, and the counts parsed from it.</summary>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []);
        }
        try
        {
            return new ResourceHour(hour, "", null, Facts(await azure.QueryAsync(workspaceId, Query, cancellation)), []);
        }
        catch (HttpRequestException ex)
        {
            string status = ex.StatusCode is { } code ? ((int)code).ToString(CultureInfo.InvariantCulture) : "nothing";
            return new ResourceHour(hour, "", null, new Dictionary<string, string?> { [NoteFact] = $"not read: the workspace answered {status}" }, []);
        }
    }

    /// <summary>The fact holding why the workspace was not read.</summary>
    public const string NoteFact = "note";

    /// <summary>The facts from the query's rows: pick_* and scan_* for each line, and the counts parsed from both.</summary>
    public static Dictionary<string, string?> Facts(IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string kind = row.GetValueOrDefault("Kind") ?? "";
            if (kind is not ("pick" or "scan"))
            {
                continue;
            }
            facts[$"{kind}_at"] = row.GetValueOrDefault("At");
            facts[$"{kind}_line"] = row.GetValueOrDefault("Line");
            facts[$"{kind}_ms"] = row.GetValueOrDefault("TookMs");
            facts[$"{kind}_ok"] = row.GetValueOrDefault("Ok");
        }
        foreach (var (key, value) in Counts(facts.GetValueOrDefault("pick_line"), facts.GetValueOrDefault("scan_line")))
        {
            facts[key] = value;
        }
        return facts;
    }

    /// <summary>
    /// The counts the two lines carry: the picker's leads, passed, confirmed and failed, its postings and boards, why the
    /// postings were blocked and the dispatch's status; the scan's boards, answered and postings.
    /// </summary>
    public static IEnumerable<(string Key, string? Value)> Counts(string? pick, string? scan)
    {
        string p = pick ?? "", s = scan ?? "";
        yield return ("leads", PickLeads().Match(p).Groups[1].Value);
        yield return ("passed", Passed().Match(p).Groups[1].Value);
        yield return ("confirmed", Confirmed().Match(p).Groups[1].Value);
        yield return ("failed", Failed().Match(p).Groups[1].Value);
        yield return ("blocked", Blocked().Match(p).Groups[1].Value);
        yield return ("dispatch", Dispatch().Match(p).Groups[1].Value);
        yield return ("pick_failed", p.Contains("FAILED:", StringComparison.Ordinal) ? "yes" : null);
        var boards = ScanBoards().Match(s);
        yield return ("boards", boards.Groups[1].Value);
        yield return ("answered", boards.Groups[2].Value);
        yield return ("postings", boards.Groups[3].Value);
    }

    /// <summary>A whole number from a fact, or null.</summary>
    public static double? Number(string? fact) =>
        double.TryParse(fact, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    [GeneratedRegex(@"(\d+) alert leads")]
    private static partial Regex PickLeads();

    [GeneratedRegex(@"(\d+) passed the gates")]
    private static partial Regex Passed();

    [GeneratedRegex(@"(\d+) confirmed by the feeds")]
    private static partial Regex Confirmed();

    [GeneratedRegex(@"(\d+) failed\b")]
    private static partial Regex Failed();

    [GeneratedRegex(@"postings blocked: ([a-z_]+)")]
    private static partial Regex Blocked();

    [GeneratedRegex(@"\| (dispatch [a-z]+ \d+)")]
    private static partial Regex Dispatch();

    [GeneratedRegex(@"board scan: (\d+) boards, (\d+) answered, (\d+) postings")]
    private static partial Regex ScanBoards();
}
// #endregion picker-source
