// The job hunt Function's run history, one process at a time, as a query (ADR-109, the addendum on the six
// processes; ADR-115): the runs of one of the seven for a window, newest first, twenty a page, each with what it
// came to, what it read and wrote as one sentence and a link to its trace, and the window's totals. The rows
// come through the IProcessRunsReader port, which Application Insights answers on the host; the words are
// ProcessWords', so the handler is tested with a fake reader and no host.
using System.Globalization;
using TheYard.Application.Shared;
using TheYard.Application.Shared.History;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

// #region process-runs
/// <summary>Port: the Function's run lines and request rows, read from Application Insights.</summary>
public interface IProcessRunsReader
{
    /// <summary>One process's runs in a window, the ones its history lists, newest first (the health route's slowest first), and the window's totals.</summary>
    /// <param name="process">One of the seven.</param>
    /// <param name="window">How far back.</param>
    /// <param name="skip">How many listed runs to pass over.</param>
    /// <param name="take">How many to answer.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation);

    /// <summary>Every process's runs folded by Central day over a window, and each process's newest run.</summary>
    /// <param name="window">How far back.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation);

    /// <summary>A link to one run's trace (its request, its run line and anything else in its operation); null when there is no workspace to link to.</summary>
    /// <param name="operationId">The run's operation id.</param>
    string? TraceHref(string operationId);

    /// <summary>A link to every run of one process; null when there is no workspace to link to.</summary>
    /// <param name="process">The process's key.</param>
    string? ProcessHref(string process);
}

/// <summary>One run as Application Insights holds it: the request row the host wrote and the run line the process wrote beside it.</summary>
/// <param name="At">When the request row says the run started, UTC.</param>
/// <param name="TookMs">How long the invocation took, in milliseconds.</param>
/// <param name="Outcome">One of RunLine's six words; for a row with no run line, success or failed from the request.</param>
/// <param name="Detail">The run line's one word beside the outcome; null for none.</param>
/// <param name="Counts">The run line's counts by name; empty for a row with no run line.</param>
/// <param name="OperationId">The operation the run's rows share; null when not read.</param>
/// <param name="HasLine">Whether the process wrote its run line; a row from before the processes wrote one has none.</param>
public sealed record ProcessRun(DateTimeOffset At, long TookMs, string Outcome, string? Detail, IReadOnlyDictionary<string, long> Counts, string? OperationId, bool HasLine)
{
    /// <summary>
    /// A run from its request row and its run line: "process:Merger outcome=success took_ms=812
    /// started=(the start, UTC) merged=3". The line's outcome, detail and counts win; a row with no line
    /// (or a line that does not parse) is success or failed by the request alone.
    /// </summary>
    /// <param name="at">The request row's start.</param>
    /// <param name="tookMs">The request row's duration.</param>
    /// <param name="ok">The request row's success.</param>
    /// <param name="line">The run line, or null.</param>
    /// <param name="operationId">The operation id.</param>
    public static ProcessRun From(DateTimeOffset at, long tookMs, bool ok, string? line, string? operationId)
    {
        string outcome = ok ? "success" : "failed";
        string? detail = null;
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        bool parsed = false;
        foreach (string part in (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            int eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }
            string name = part[..eq], value = part[(eq + 1)..];
            if (name == "outcome" && ProcessWords.Outcomes.Contains(value))
            {
                (outcome, parsed) = (value, true);
            }
            else if (name == "detail")
            {
                // The writer keeps a detail to one word; the reader holds it to the same rule, so a line
                // that did not come through RunLine prints nothing it should not.
                detail = value.Length is > 0 and <= 60 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? value : null;
            }
            else if (name is not ("took_ms" or "started") && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
            {
                counts[name] = count;
            }
        }
        return new ProcessRun(at, tookMs, outcome, detail, counts, operationId, parsed);
    }
}

/// <summary>What a window's runs added up to.</summary>
/// <param name="Runs">Every run.</param>
/// <param name="Shown">The runs the history lists (for the merger, the clock and the card, the ones that did something).</param>
/// <param name="Acted">Runs whose line says they came to success or late: merged, dispatched, wrote, appended.</param>
/// <param name="Late">Runs that came to late.</param>
/// <param name="Failed">Runs that came to failed.</param>
/// <param name="Blocked">Runs that came to blocked.</param>
/// <param name="Planned">Runs that came to planned (shadow).</param>
/// <param name="P95Ms">The 95th percentile of how long a run took; null when there were none.</param>
public sealed record ProcessTally(long Runs, long Shown, long Acted, long Late, long Failed, long Blocked, long Planned, double? P95Ms)
{
    /// <summary>No runs.</summary>
    public static readonly ProcessTally None = new(0, 0, 0, 0, 0, 0, 0, null);
}

/// <summary>One process's runs on one Central day.</summary>
/// <param name="Process">The process's key.</param>
/// <param name="Day">The Central day.</param>
/// <param name="Tally">What its runs added up to.</param>
public sealed record ProcessDay(string Process, DateOnly Day, ProcessTally Tally);

/// <summary>One page of one process's runs, with the window's totals.</summary>
/// <param name="Available">False when nothing could be read; <paramref name="Note"/> says why.</param>
/// <param name="Note">Why nothing was read; null when it was.</param>
/// <param name="Rows">The page's runs.</param>
/// <param name="Totals">The window's totals.</param>
public sealed record ProcessRunsRead(bool Available, string? Note, IReadOnlyList<ProcessRun> Rows, ProcessTally Totals);

/// <summary>Every process's days and newest run over a window.</summary>
/// <param name="Available">False when nothing could be read; <paramref name="Note"/> says why.</param>
/// <param name="Note">Why nothing was read; null when it was.</param>
/// <param name="Days">Each process's runs by Central day.</param>
/// <param name="Last">Each process's newest run, by key; a process with no run in the window is absent.</param>
public sealed record ProcessFold(bool Available, string? Note, IReadOnlyList<ProcessDay> Days, IReadOnlyDictionary<string, ProcessRun> Last)
{
    /// <summary>Nothing read, and why.</summary>
    /// <param name="note">Why.</param>
    public static ProcessFold Unread(string note) => new(false, note, [], new Dictionary<string, ProcessRun>(StringComparer.Ordinal));
}

/// <summary>Asks for one page of one process's runs.</summary>
/// <param name="Process">The process's key from the route.</param>
/// <param name="Window">24h, 7d or 30d; 7d for anything else.</param>
/// <param name="Page">The page, from one; one for anything less, and MaxPage for anything more.</param>
public sealed record ReadProcessRunsQuery(string Process, string? Window, int? Page);

/// <summary>One page of a process's run history, as the card's disclosure draws it.</summary>
/// <param name="Process">The process's key.</param>
/// <param name="Title">What the card calls it.</param>
/// <param name="Window">The window read.</param>
/// <param name="Windows">The windows the history offers.</param>
/// <param name="Page">The page read.</param>
/// <param name="PageSize">Runs a page.</param>
/// <param name="Available">False when nothing could be read.</param>
/// <param name="Note">Why nothing was read; null when it was.</param>
/// <param name="Rows">The page's runs.</param>
/// <param name="More">Whether a next page has runs.</param>
/// <param name="Count">The summary's count: "188 runs in 7 days, the newest 20 shown".</param>
/// <param name="Totals">The foot's totals: "188 in 7 days · 2 late · 2 blocked · 0 failed".</param>
/// <param name="AllHref">Every run of the process in Application Insights; null when there is no link.</param>
public sealed record ProcessRunsPage(string Process, string Title, string Window, IReadOnlyList<string> Windows, int Page, int PageSize, bool Available, string? Note, IReadOnlyList<ProcessRunRow> Rows, bool More, string Count, string Totals, string? AllHref);

/// <summary>One run as a row of the history.</summary>
/// <param name="AtMs">When it started, milliseconds since the epoch.</param>
/// <param name="Day">Its Central day as the table's day row prints it: "Sat 24 Oct".</param>
/// <param name="When">Its Central time: "11:20".</param>
/// <param name="Took">How long it took in words: "1 min 31 s".</param>
/// <param name="Outcome">The outcome word.</param>
/// <param name="Came">What it came to in words: "late, 1 min 2 s after its slot".</param>
/// <param name="Tone">good, info, warning, alarm or quiet.</param>
/// <param name="Sentence">What it read and wrote, counts only.</param>
/// <param name="TraceHref">Its trace; null when there is no link.</param>
public sealed record ProcessRunRow(long AtMs, string Day, string When, string Took, string Outcome, string Came, string Tone, string Sentence, string? TraceHref);

/// <summary>One page of one process's runs; null for a name that is not one of the seven.</summary>
/// <param name="runs">The run lines and request rows.</param>
public sealed class ReadProcessRunsHandler(IProcessRunsReader runs) : IQueryHandler<ReadProcessRunsQuery, ProcessRunsPage?>
{
    /// <summary>Runs a page.</summary>
    public const int PageSize = 20;

    /// <summary>The last page the history reads: a thousand runs back; a page past it is read as this one.</summary>
    public const int MaxPage = 50;

    /// <summary>The page, or null when no process has the name.</summary>
    /// <param name="query">The process, the window and the page.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ProcessRunsPage?> HandleAsync(ReadProcessRunsQuery query, CancellationToken cancellation)
    {
        if (FunctionProcesses.Find(query.Process) is not { } process)
        {
            return null;
        }
        var (length, _, window) = MachineWindows.Parse(query.Window) ?? MachineWindows.Parse("7d")!.Value;
        int page = Math.Clamp(query.Page ?? 1, 1, MaxPage);
        var read = await runs.RunsAsync(process, length, (page - 1) * PageSize, PageSize, cancellation);
        string words = ProcessWords.WindowWords(window);
        // The health route's history is its slowest runs; within a page they are drawn newest first, so the
        // table's day rows group them.
        var ordered = process.Key == "MergerHealth" ? read.Rows.OrderByDescending(run => run.At) : (IEnumerable<ProcessRun>)read.Rows;
        var rows = ordered.Select(run => new ProcessRunRow(
            run.At.ToUnixTimeMilliseconds(),
            ProcessWords.Day(run.At),
            ProcessWords.Clock(run.At),
            ProcessWords.Took(run.TookMs),
            run.Outcome,
            ProcessWords.Came(run),
            ProcessWords.Tone(run.Outcome),
            ProcessWords.Sentence(process.Key, run),
            run.OperationId is { } id ? runs.TraceHref(id) : null)).ToList();
        return new ProcessRunsPage(
            process.Key,
            process.Title,
            window,
            ["24h", "7d", "30d"],
            page,
            PageSize,
            read.Available,
            read.Note,
            rows,
            (page - 1) * PageSize + rows.Count < read.Totals.Shown,
            ProcessWords.CountLine(process, read.Totals, words),
            ProcessWords.TotalsLine(read.Totals, words),
            runs.ProcessHref(process.Key));
    }
}
// #endregion process-runs
