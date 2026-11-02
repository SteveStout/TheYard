using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>One lane's run, as the orchestrator hands it over.</summary>
/// <param name="Lane">The lane.</param>
/// <param name="Day">The Central day.</param>
/// <param name="Dry">True for a dry run.</param>
/// <param name="Before">The tracker's counts at the start.</param>
/// <param name="Lines">Lines the DONE note carries from the start of the run.</param>
/// <param name="LiveCount">The live invite count the recount read, which the program holds the lane to.</param>
public sealed record LaneStart(Lane Lane, DateOnly Day, bool Dry, OutreachCounts? Before, IReadOnlyList<string> Lines, int? LiveCount = null);

/// <summary>
/// Runs one lane: writes the worklist, starts the model, cuts the usage, holds what the model says to its evidence and
/// writes the records. The model never writes a record; it writes only in its own folder, and everything a merger reads
/// comes from here. A send with a thin claim is still recorded, marked UNVERIFIED. Every send in progress.tsv, the line
/// the lane doc makes the model keep after every send, counts whether or not the result names it, so a timeout cannot
/// lose one. The records are written before the usage is reported, and on a token nothing cancels: a report that hangs
/// or a service that is stopping must never cost a row, because an unrecorded send goes out again tomorrow.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="claude">The CLI.</param>
/// <param name="usage">The usage report.</param>
/// <param name="config">The settings.</param>
/// <param name="time">The clock.</param>
public sealed partial class LaneRunner(IShare share, IClaudeCli claude, IUsageSink usage, ServerConfig config, TimeProvider time)
{
    /// <summary>Writes a lane that did not run: its DONE note says why.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="held">Why it did not run.</param>
    /// <param name="ct">Stops the write.</param>
    public async Task<LaneReport> HoldAsync(LaneStart start, string held, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        LaneConfig lane = config.For(start.Lane);
        var report = new LaneReport(start.Lane, lane.Model, start.Dry, now, now, start.Before, [], [], null, [], start.Lines, held);
        await share.WriteAsync($"{RunFolder(start, lane)}/DONE.md", DoneNote.Render(report), ct);
        return report;
    }

    /// <summary>
    /// Why a lane may not start because an earlier run of it today died: a progress file with no DONE note is sends with
    /// no records, and a person has to read them before the same worklist is built again.
    /// </summary>
    /// <param name="start">The lane.</param>
    /// <param name="ct">Stops the read.</param>
    public async Task<string?> CrashedAsync(LaneStart start, CancellationToken ct)
    {
        string folder = RunFolder(start, config.For(start.Lane));
        return await share.ExistsAsync($"{folder}/progress.tsv", ct) && !await share.ExistsAsync($"{folder}/DONE.md", ct)
            ? $"an earlier run left {folder}/progress.tsv and no DONE note; read it before this lane runs again"
            : null;
    }

    /// <summary>Runs the lane to its end and writes its records.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="list">Its worklist.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<LaneReport> RunAsync(LaneStart start, Worklist list, CancellationToken ct)
    {
        DateTimeOffset began = time.GetUtcNow();
        LaneConfig lane = config.For(start.Lane);
        string folder = RunFolder(start, lane);
        var lines = new List<string>(start.Lines);
        await share.WriteAsync($"{folder}/worklist.tsv", WorklistTsv.Render(list.Rows), ct);
        foreach (WorkRow row in list.Rows.Where(row => row.TextFile.Length > 0))
        {
            await share.WriteAsync($"{folder}/{row.TextFile}", list.Texts[row.Row] + "\n", ct);
        }
        if (list.Blockers.Count > 0)
        {
            await share.WriteAsync($"{folder}/APPLY-BLOCKERS-{start.Day:yyyy-MM-dd}.md", Blockers.Render(start.Day, list.Blockers), ct);
            lines.Add($"{list.Blockers.Count} postings on the day's blockers list: {folder}/APPLY-BLOCKERS-{start.Day:yyyy-MM-dd}.md");
        }

        if (start.Lane == Lane.Apply)
        {
            await WriteResumeAsync(folder, ct);
        }

        LaneResult? result = null;
        IReadOnlyList<SendRecord> sends = [];
        if (list.Rows.Count == 0)
        {
            lines.Add("Nothing on the worklist; no model run.");
        }
        else
        {
            string runFolder = share.Full(folder);
            var request = new ClaudeRequest(
                ClaudeArguments.For(lane, start.Lane, share.Root, runFolder, start.Dry),
                runFolder,
                $"{runFolder}/stream.jsonl",
                TimeSpan.FromMinutes(lane.TimeoutMinutes));
            ClaudeRun run = await claude.RunAsync(request, ct);
            lines.Add($"claude -p exit {run.ExitCode}{(run.TimedOut ? ", killed at the wall-clock limit or by a stop" : string.Empty)}.");
            CancellationToken finish = CancellationToken.None;
            result = await ReadResultAsync(run, start, folder, list, lines, finish);
            sends = await RecordAsync(start, folder, lane.Model, list, result, lines, finish);
            await ReportUsageAsync(run, start, folder, began, lines, finish);
        }
        var report = new LaneReport(start.Lane, lane.Model, start.Dry, began, time.GetUtcNow(), start.Before, list.Rows, list.Dropped, result, sends, lines, null);
        await share.WriteAsync($"{folder}/DONE.md", DoneNote.Render(report), CancellationToken.None);
        return report;
    }

    private async Task WriteResumeAsync(string folder, CancellationToken ct)
    {
        var resumes = await share.ListAsync(string.Empty, "Steven_Stout_Resume_*.pdf", ct);
        string newest = resumes.Select(file => file.Path).OrderDescending(StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
        await share.WriteAsync($"{folder}/resume.txt", newest.Length == 0 ? string.Empty : share.Full(newest) + "\n", ct);
    }

    private string RunFolder(LaneStart start, LaneConfig lane) => RunSlot.RunFolder(start.Day, start.Lane, lane.Model, start.Dry);

    private async Task<LaneResult?> ReadResultAsync(ClaudeRun run, LaneStart start, string folder, Worklist list, List<string> lines, CancellationToken ct)
    {
        string? structured = run.ResultLine is null ? null : JsonNode.Parse(run.ResultLine)?["structured_output"]?.ToJsonString();
        LaneResult? result = structured is null ? null : LaneResult.Parse(structured);
        IReadOnlyList<RowResult> progress = await ProgressAsync(folder, list, ct);
        if (result is null)
        {
            lines.Add("The model gave no structured result; progress.tsv read instead, and every send in it stands UNVERIFIED.");
            return progress.Count == 0 ? null : new LaneResult(progress, start.LiveCount, "none", null, null);
        }
        var missing = progress.Where(row => result.Rows.All(reported => reported.Row != row.Row)).ToList();
        if (missing.Count > 0)
        {
            lines.Add($"{missing.Count} sends in progress.tsv were not in the result; they stand UNVERIFIED.");
        }
        return result with { Rows = [.. result.Rows, .. missing], LiveInviteCount = start.LiveCount ?? result.LiveInviteCount };
    }

    private async Task ReportUsageAsync(ClaudeRun run, LaneStart start, string folder, DateTimeOffset began, List<string> lines, CancellationToken ct)
    {
        if (run.ResultLine is not { } line)
        {
            return;
        }
        try
        {
            string cut = UsageCut.Json(line);
            await share.WriteAsync($"{folder}/usage.json", cut, ct);
            var interim = new LaneReport(start.Lane, config.For(start.Lane).Model, start.Dry, began, time.GetUtcNow(), start.Before, [], [], null, [], [], null);
            lines.Add(await usage.ReportAsync(UsageCut.Document(cut, interim, RegistryRows.Lane + began.ToUnixTimeSeconds()), ct));
        }
        catch (IOException failure)
        {
            lines.Add($"usage not written: {failure.Message}");
        }
    }

    private async Task<IReadOnlyList<RowResult>> ProgressAsync(string folder, Worklist list, CancellationToken ct)
    {
        if (!await share.ExistsAsync($"{folder}/progress.tsv", ct))
        {
            return [];
        }
        var rows = new List<RowResult>();
        foreach (string line in (await share.ReadAsync($"{folder}/progress.tsv", ct)).Split('\n'))
        {
            string[] cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length >= 2 && cells[1] == "sent" && int.TryParse(cells[0], out int row) && list.Rows.Any(r => r.Row == row))
            {
                rows.Add(new RowResult(row, cells[1], "from progress.tsv", cells.Length > 2 && cells[2].Length > 0 ? cells[2] : null, null, null, null));
            }
        }
        return rows;
    }

    private async Task<IReadOnlyList<SendRecord>> RecordAsync(LaneStart start, string folder, string model, Worklist list, LaneResult? result, List<string> lines, CancellationToken ct)
    {
        var sends = new List<SendRecord>();
        foreach (RowResult claimed in (result?.Rows ?? []).Where(row => row.Outcome == "sent"))
        {
            WorkRow? row = list.Rows.FirstOrDefault(r => r.Row == claimed.Row);
            if (row is null)
            {
                lines.Add($"The model reported row {claimed.Row}, which is not on the worklist.");
                continue;
            }
            if (start.Dry)
            {
                lines.Add($"DEFECT: a dry run reported row {claimed.Row} as sent; no record written, read the page.");
                continue;
            }
            RowResult named = claimed.Person is { Length: > 0 } ? claimed : claimed with { Person = row.Person };
            string text = row.Action == WorkAction.Apply ? string.Empty : EvidenceCheck.Filled(list.Texts[row.Row], named.Person);
            var missing = EvidenceCheck.Missing(row.Action, text, named);
            string shot = await CopyShotAsync(folder, row.Row, named.Evidence?.Screenshot, lines, ct);
            var record = new SendRecord(row, named, text, missing, model, shot);
            sends.Add(record);
            await WriteRowAsync(record, sends.Count, lines, ct);
        }
        return sends;
    }

    private async Task WriteRowAsync(SendRecord record, int order, List<string> lines, CancellationToken ct)
    {
        DateTimeOffset at = time.GetUtcNow().AddSeconds(order);
        string what = record.Reported.Person is { Length: > 0 } person ? person : record.Row.Company;
        string path = $"inbox/tracker-inbox/{TrackerRowFile.Name(at, what)}";
        try
        {
            await share.WriteNewAsync(path, TrackerRowFile.Line(record, at), ct);
            lines.Add($"Row written and read back: {path}");
        }
        catch (IOException failure)
        {
            lines.Add($"TRACKER ROW NOT WRITTEN for row {record.Row.Row} ({what}): {failure.Message}. Write it by hand from this note.");
        }
    }

    private async Task<string> CopyShotAsync(string folder, int row, string? from, List<string> lines, CancellationToken ct)
    {
        if (from is null || !Shot().IsMatch(from))
        {
            return from ?? string.Empty;
        }
        string relative = $"{folder}/shots/row-{row:00}{Path.GetExtension(from)}";
        try
        {
            await share.CopyInAsync(from, relative, ct);
            return relative;
        }
        catch (IOException failure)
        {
            lines.Add($"Screenshot for row {row} not copied: {failure.Message}");
            return from;
        }
    }

    [GeneratedRegex(@"^/tmp/claude-chrome-screenshots-[\w-]+/[\w.-]+\.(jpg|jpeg|png)$")]
    private static partial Regex Shot();
}

/// <summary>The day's APPLY-BLOCKERS list, in the existing shape: what stuck, and the one thing that clears it.</summary>
public static class Blockers
{
    /// <summary>The whole file.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="postings">The postings behind a wall.</param>
    public static string Render(DateOnly day, IReadOnlyList<Posting> postings)
    {
        var lines = new List<string>
        {
            "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).",
            string.Empty,
            $"# APPLY-BLOCKERS {day:yyyy-MM-dd} ({RegistryRows.Lane})",
            string.Empty,
            "Each one: open the link, click Apply, sign in or create the account, stop on the first form page, say \"fill <company>\".",
            string.Empty,
        };
        int number = 0;
        foreach (Posting posting in postings)
        {
            lines.Add($"## {++number}. {posting.Company}, {posting.Role}");
            lines.Add($"- Where it stuck: {(posting.Board.Length > 0 ? posting.Board : "not a form this lane may use")}.");
            lines.Add($"- Link: {posting.Link}");
            lines.Add($"- Source: {posting.Source}");
            lines.Add(string.Empty);
        }
        return string.Join('\n', lines);
    }
}
