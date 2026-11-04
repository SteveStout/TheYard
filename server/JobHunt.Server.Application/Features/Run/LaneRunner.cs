using System.Text.Json;
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
/// the lane doc makes the model keep after every send, counts whether or not the result names it and whatever the result
/// calls it, so neither a timeout nor a change of heart can lose one. The records are written before the usage is reported, and on a token nothing cancels: a report that hangs
/// or a service that is stopping must never cost a row, because an unrecorded send goes out again tomorrow.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="claude">The CLI.</param>
/// <param name="usage">The usage report.</param>
/// <param name="config">The settings.</param>
/// <param name="time">The clock.</param>
public sealed partial class LaneRunner(IShare share, IClaudeCli claude, IUsageSink usage, ServerConfig config, TimeProvider time)
{
    private const int RowAttempts = 3;
    private const int Spacing = 100;
    private const string NotWritten = "TRACKER ROW NOT WRITTEN";

    /// <summary>
    /// Writes a lane that did not run: its DONE note says why. A folder a crash left is the exception, because a DONE note
    /// there would read as the crash being dealt with, so the hold goes in HELD.md and the lane stays held.
    /// </summary>
    /// <param name="start">The lane.</param>
    /// <param name="held">Why it did not run.</param>
    /// <param name="ct">Stops the write.</param>
    public async Task<LaneReport> HoldAsync(LaneStart start, string held, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        LaneConfig lane = config.For(start.Lane);
        var report = new LaneReport(start.Lane, lane.Model, start.Dry, now, now, start.Before, [], [], null, [], start.Lines, held);
        string folder = RunFolder(start, lane);
        string note = await share.ExistsAsync($"{folder}/progress.tsv", ct) ? "HELD.md" : "DONE.md";
        await share.WriteAsync($"{folder}/{note}", DoneNote.Render(report), ct);
        return report;
    }

    /// <summary>
    /// Why a lane may not start because an earlier run of it died, lost a row or left a defect, on any day: a progress file
    /// with no DONE note, or a note with a defect or an unwritten row, may be a send with no record, and a person has to
    /// settle it before the same worklist is built again. The hold never runs out on its own.
    /// </summary>
    /// <param name="start">The lane.</param>
    /// <param name="ct">Stops the read.</param>
    public async Task<string?> CrashedAsync(LaneStart start, CancellationToken ct)
    {
        foreach (string day in await share.FoldersAsync("nightly", "20*", ct))
        {
            foreach (string folder in await share.FoldersAsync(day, $"{RunSlot.Hour}-{RunSlot.Word(start.Lane)}-*", ct))
            {
                if (folder.EndsWith("-dry", StringComparison.Ordinal))
                {
                    continue;
                }
                if (await share.ExistsAsync($"{folder}/DONE.md", ct) && Unsettled().IsMatch(await share.ReadAsync($"{folder}/DONE.md", ct)))
                {
                    return $"{folder}/DONE.md carries a defect line or an unwritten tracker row, which may be a send with no record. Settle it, then take that line out of the note, before this lane runs again";
                }
                if (await share.ExistsAsync($"{folder}/progress.tsv", ct) && !await FinishedAsync(folder, ct))
                {
                    return $"an earlier run left {folder}/progress.tsv and no DONE note: sends with no records. Write their rows, then rename that file, before this lane runs again";
                }
            }
        }
        return null;
    }

    private async Task<bool> FinishedAsync(string folder, CancellationToken ct) =>
        await share.ExistsAsync($"{folder}/DONE.md", ct) && !(await share.ReadAsync($"{folder}/DONE.md", ct)).Contains("\nHELD: ", StringComparison.Ordinal);

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

        LaneResult? result = null;
        IReadOnlyList<SendRecord> sends = [];
        bool cutShort = false;
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
            cutShort = run.TimedOut || LaneResult.FromLine(run.ResultLine) is null;
            result = await ReadResultAsync(run, start, folder, list, lines, finish);
            sends = await RecordAsync(start, folder, lane.Model, list, result, lines, finish);
            await ReportUsageAsync(run, start, folder, began, lines, finish);
        }
        var report = new LaneReport(start.Lane, lane.Model, start.Dry, began, time.GetUtcNow(), start.Before, list.Rows, list.Dropped, result, sends, lines, null, cutShort);
        return await FinishAsync(report, folder, CancellationToken.None);
    }

    private async Task<LaneReport> FinishAsync(LaneReport report, string folder, CancellationToken ct)
    {
        try
        {
            await share.WriteAsync($"{folder}/DONE.md", DoneNote.Render(report), ct);
            return report;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return report with { Lines = [.. report.Lines, $"DEFECT: DONE.md not written: {failure.Message}"] };
        }
    }

    private string RunFolder(LaneStart start, LaneConfig lane) => RunSlot.RunFolder(start.Day, start.Lane, lane.Model, start.Dry);

    private async Task<LaneResult?> ReadResultAsync(ClaudeRun run, LaneStart start, string folder, Worklist list, List<string> lines, CancellationToken ct)
    {
        LaneResult? result = LaneResult.FromLine(run.ResultLine);
        IReadOnlyList<RowResult> progress = await ProgressAsync(folder, lines, ct);
        if (result is null)
        {
            lines.Add("The model gave no structured result; progress.tsv read instead, and every send in it stands UNVERIFIED.");
            return progress.Count == 0 ? null : new LaneResult(progress, start.LiveCount, "none", null, null);
        }
        var missing = progress.Where(row => !result.Rows.Any(reported => reported.Row == row.Row && reported.Outcome == "sent")).ToList();
        if (missing.Count > 0)
        {
            lines.Add($"{missing.Count} sends in progress.tsv were not reported as sent; they stand UNVERIFIED.");
        }
        var kept = result.Rows.Where(reported => missing.All(row => row.Row != reported.Row));
        return result with { Rows = [.. kept, .. missing], LiveInviteCount = start.LiveCount ?? result.LiveInviteCount };
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
        catch (Exception failure) when (failure is IOException or JsonException)
        {
            lines.Add($"usage not written: {failure.Message}");
        }
    }

    private async Task<IReadOnlyList<RowResult>> ProgressAsync(string folder, List<string> lines, CancellationToken ct)
    {
        if (!await share.ExistsAsync($"{folder}/progress.tsv", ct))
        {
            return [];
        }
        (IReadOnlyList<RowResult> rows, IReadOnlyList<string> unread) = ProgressFile.Read(await share.ReadAsync($"{folder}/progress.tsv", ct));
        lines.AddRange(unread.Select(line => $"DEFECT: progress.tsv line not read, read the page: {line}"));
        return rows;
    }

    private async Task<IReadOnlyList<SendRecord>> RecordAsync(LaneStart start, string folder, string model, Worklist list, LaneResult? result, List<string> lines, CancellationToken ct)
    {
        var sends = new List<SendRecord>();
        foreach (RowResult claimed in (result?.Rows ?? []).Where(row => row.Outcome == "sent").DistinctBy(row => row.Row))
        {
            WorkRow? row = list.Rows.FirstOrDefault(r => r.Row == claimed.Row);
            if (row is null)
            {
                lines.Add($"DEFECT: the model reported a send on row {claimed.Row}, which is not on the worklist ({claimed.Person} {claimed.ProfileUrl}). No record written, read the page.");
                continue;
            }
            if (start.Dry)
            {
                lines.Add($"DEFECT: a dry run reported row {claimed.Row} as sent; no record written, read the page.");
                continue;
            }
            RowResult named = row.Action == WorkAction.Apply ? claimed with { Person = null } : claimed.Person is { Length: > 0 } ? claimed : claimed with { Person = row.Person };
            string text = row.Action == WorkAction.Apply ? string.Empty : EvidenceCheck.Filled(list.Texts[row.Row], named.Person);
            var missing = EvidenceCheck.Missing(row.Action, text, named).Concat(EvidenceCheck.WrongPerson(row, named) is { } wrong ? [wrong] : []).ToList();
            string shot = await CopyShotAsync(folder, row.Row, named.Evidence?.Screenshot, lines, ct);
            var record = new SendRecord(row, named, text, missing, model, shot);
            sends.Add(record);
            await WriteRowAsync(record, sends.Count, lines, ct);
        }
        return sends;
    }

    private async Task WriteRowAsync(SendRecord record, int order, List<string> lines, CancellationToken ct)
    {
        string what = record.Reported.Person is { Length: > 0 } person ? person : record.Row.Company;
        for (int attempt = 1; ; attempt++)
        {
            DateTimeOffset at = time.GetUtcNow().AddSeconds(order + ((attempt - 1) * Spacing));
            string path = $"inbox/tracker-inbox/{TrackerRowFile.Name(at, what)}";
            try
            {
                await share.WriteNewAsync(path, TrackerRowFile.Line(record, at), ct);
                lines.Add($"Row written and read back: {path}");
                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (await share.ExistsAsync(path, CancellationToken.None))
                {
                    lines.Add($"{NotWritten} cleanly for row {record.Row.Row} ({what}): {path} holds what reached the share, {failure.Message}. Read it and fix it by hand from this note.");
                    return;
                }
                if (attempt == RowAttempts)
                {
                    lines.Add($"{NotWritten} for row {record.Row.Row} ({what}): {failure.Message}. Write it by hand from this note.");
                    return;
                }
            }
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
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            lines.Add($"Screenshot for row {row} not copied: {failure.Message}");
            return from;
        }
    }

    [GeneratedRegex(@"DEFECT:|TRACKER ROW NOT WRITTEN")]
    private static partial Regex Unsettled();

    [GeneratedRegex(@"^/tmp/claude-chrome-screenshots-[\w-]+/[\w.-]+\.(jpg|jpeg|png)$")]
    private static partial Regex Shot();
}
