using System.Globalization;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>What the feed lane works with beside the share.</summary>
/// <param name="Claude">The CLI.</param>
/// <param name="Prose">check_aiisms.py.</param>
/// <param name="Usage">The usage report.</param>
public sealed record FeedTools(IClaudeCli Claude, IProseCheck Prose, UsageReport Usage);

/// <summary>
/// Runs the hot-manager feed, the first lane of the morning. The model reads the share and LinkedIn and writes only
/// candidates.json in its run folder; the program holds every candidate to the gates (FeedQueue), proofreads each text
/// with check_aiisms.py, and writes the day's queue, which the invites and messages lanes read next. A live run writes
/// linkedin-proof/QUEUE-hotmgr-&lt;day&gt;.md and never over one already there; a dry run writes the same file into its run
/// folder only. The feed mints nothing, sends nothing and writes no tracker row. A person or company named in
/// linkedin-proof/FEED-HOLD.md waits on Steve and is never queued by the feed. In a slot run for one caught role the
/// model is told that role, a candidate at any other company is dropped before the gates read any, only the first
/// that passes them is queued, a budget-flagged role's text may ask the
/// timeline question, and the queue is QUEUE-pounce-&lt;day&gt;-&lt;key&gt;.md, so the role's blocks are a file of their own.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="tools">The CLI, the proofread and the usage report.</param>
/// <param name="config">The settings.</param>
/// <param name="time">The clock.</param>
public sealed class FeedLane(IShare share, FeedTools tools, ServerConfig config, TimeProvider time)
{
    /// <summary>The file naming the people the feed leaves for Steve.</summary>
    public const string HoldFile = "linkedin-proof/FEED-HOLD.md";

    /// <summary>The queue the feed writes for a day, relative to the share or to its run folder.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="role">The caught role a slot run works, or null for the morning's feed.</param>
    public static string QueueName(DateOnly day, RoleHandoff? role = null) => role is null
        ? string.Create(CultureInfo.InvariantCulture, $"QUEUE-hotmgr-{day:yyyy-MM-dd}.md")
        : string.Create(CultureInfo.InvariantCulture, $"QUEUE-pounce-{day:yyyy-MM-dd}-{role.Key}.md");

    /// <summary>Runs the feed and writes its queue and DONE note.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="reads">The morning's reads, for the gates and the open queue blocks.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<LaneReport> RunAsync(LaneStart start, DayReads reads, CancellationToken ct)
    {
        DateTimeOffset began = time.GetUtcNow();
        LaneConfig lane = config.For(Lane.Feed);
        string folder = RunSlot.RunFolder(start.Day, Lane.Feed, lane.Model, start.Dry, start.Slot);
        string runFolder = share.Full(folder);
        var lines = new List<string>(start.Lines);
        var request = new ClaudeRequest(ClaudeArguments.For(lane, Lane.Feed, share.Root, runFolder, start.Dry, start.Role), runFolder, $"{runFolder}/stream.jsonl", TimeSpan.FromMinutes(lane.TimeoutMinutes));
        ClaudeRun run = await tools.Claude.RunAsync(request, ct);
        lines.Add($"claude -p exit {run.ExitCode}{(run.TimedOut ? ", killed at the wall-clock limit or by a stop" : string.Empty)}.");
        CancellationToken finish = CancellationToken.None;
        LaneResult? result = LaneResult.FromLine(run.ResultLine);
        if (result?.Rows.Any(row => row.Outcome == "sent") == true)
        {
            lines.Add("DEFECT: the feed reported a send; it may send nothing. Read the page.");
        }

        string candidatesPath = $"{folder}/candidates.json";
        IReadOnlyList<FeedCandidate> candidates = await share.ExistsAsync(candidatesPath, finish) ? FeedQueue.Parse(await share.ReadAsync(candidatesPath, finish)) : [];
        lines.Add($"{candidates.Count} candidates read from {candidatesPath}.");
        var dropped = new List<Dropped>();
        if (start.Role is { } role)
        {
            dropped.AddRange(candidates.Where(candidate => !ApplicationCap.Same(candidate.Company ?? string.Empty, role.Company))
                .Select(candidate => new Dropped($"{candidate.Person}, {candidate.Company}".Trim(' ', ','), $"not the caught role's company ({role.Company})")));
            candidates = [.. candidates.Where(candidate => ApplicationCap.Same(candidate.Company ?? string.Empty, role.Company))];
        }
        FeedCut cut = FeedQueue.Cut(candidates, reads.Gate, reads.Queues, reads.Holds, start.Role?.Budget == true);
        dropped.AddRange(cut.Dropped);
        var kept = new List<FeedCandidate>();
        foreach (FeedCandidate candidate in cut.Kept)
        {
            if (start.Role is not null && kept.Count > 0)
            {
                dropped.Add(new Dropped($"{candidate.Person}, {candidate.Company}".Trim(' ', ','), "one person a caught role: the first that passed is queued"));
            }
            else if (await tools.Prose.CleanAsync(candidate.Text!, finish))
            {
                kept.Add(candidate);
            }
            else
            {
                dropped.Add(new Dropped($"{candidate.Person}, {candidate.Company}".Trim(' ', ','), "text failed check_aiisms.py"));
            }
        }
        await WriteQueueAsync(start, folder, kept, lines, finish);
        await tools.Usage.WriteAsync(run, start, folder, began, lines, finish);
        var report = new LaneReport(Lane.Feed, lane.Model, start.Dry, began, time.GetUtcNow(), start.Before, [], dropped, result, [], lines, null, run.TimedOut || result is null) { Queued = kept.Count };
        await share.WriteAsync($"{folder}/DONE.md", DoneNote.Render(report), finish);
        return report;
    }

    private async Task WriteQueueAsync(LaneStart start, string folder, IReadOnlyList<FeedCandidate> kept, List<string> lines, CancellationToken ct)
    {
        if (kept.Count == 0)
        {
            lines.Add("No candidate passed the gates; no queue written.");
            return;
        }
        string name = QueueName(start.Day, start.Role);
        string path = start.Dry ? $"{folder}/{name}" : $"linkedin-proof/{name}";
        if (!start.Dry && await share.ExistsAsync(path, ct))
        {
            lines.Add($"{path} is already on the share; left as it was, nothing queued by this run.");
            return;
        }
        string? heading = start.Role is { } role ? $"{name[..^3]}: one caught role, {role.Company}, {role.Title}" : null;
        await share.WriteNewAsync(path, FeedQueue.Render(start.Day, RunSlot.Local(time.GetUtcNow()), folder, kept, heading, start.Role?.Budget == true), ct);
        lines.Add($"Queue written and read back: {path}, {kept.Count} blocks.");
        lines.AddRange(kept.Select((block, index) => $"Block {index + 1}: {(block.Person!.Length > 0 ? block.Person + ", " : string.Empty)}{block.Company} ({block.Seat}), hot {block.Hot}, {block.Action}."));
    }
}
