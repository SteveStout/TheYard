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
/// linkedin-proof/FEED-HOLD.md waits on Steve and is never queued by the feed.
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
    public static string QueueName(DateOnly day) => string.Create(CultureInfo.InvariantCulture, $"QUEUE-hotmgr-{day:yyyy-MM-dd}.md");

    /// <summary>Runs the feed and writes its queue and DONE note.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="reads">The morning's reads, for the gates and the open queue blocks.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<LaneReport> RunAsync(LaneStart start, DayReads reads, CancellationToken ct)
    {
        DateTimeOffset began = time.GetUtcNow();
        LaneConfig lane = config.For(Lane.Feed);
        string folder = RunSlot.RunFolder(start.Day, Lane.Feed, lane.Model, start.Dry);
        string runFolder = share.Full(folder);
        var lines = new List<string>(start.Lines);
        var request = new ClaudeRequest(ClaudeArguments.For(lane, Lane.Feed, share.Root, runFolder, start.Dry), runFolder, $"{runFolder}/stream.jsonl", TimeSpan.FromMinutes(lane.TimeoutMinutes));
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
        FeedCut cut = FeedQueue.Cut(candidates, reads.Gate, reads.Queues, reads.Holds);
        var dropped = new List<Dropped>(cut.Dropped);
        var kept = new List<FeedCandidate>();
        foreach (FeedCandidate candidate in cut.Kept)
        {
            if (await tools.Prose.CleanAsync(candidate.Text!, finish))
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
        string path = start.Dry ? $"{folder}/{QueueName(start.Day)}" : $"linkedin-proof/{QueueName(start.Day)}";
        if (!start.Dry && await share.ExistsAsync(path, ct))
        {
            lines.Add($"{path} is already on the share; left as it was, nothing queued by this run.");
            return;
        }
        await share.WriteNewAsync(path, FeedQueue.Render(start.Day, RunSlot.Local(time.GetUtcNow()), folder, kept), ct);
        lines.Add($"Queue written and read back: {path}, {kept.Count} blocks.");
        lines.AddRange(kept.Select((block, index) => $"Block {index + 1}: {(block.Person!.Length > 0 ? block.Person + ", " : string.Empty)}{block.Company} ({block.Seat}), hot {block.Hot}, {block.Action}."));
    }
}
