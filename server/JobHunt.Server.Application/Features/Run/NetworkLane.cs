namespace JobHunt.Server.Application.Features.Run;

/// <summary>What the network lane works with beside the share.</summary>
/// <param name="Claude">The CLI.</param>
/// <param name="Usage">The usage report.</param>
public sealed record NetworkTools(IClaudeCli Claude, UsageReport Usage);

/// <summary>
/// Runs the network log's LinkedIn half, after the messages lane. The model reads Messaging back to the high-water mark
/// the program hands it, then the backlog on below the floor until its window's end, and writes, in its run folder only, network-threads.tsv (one row a thread where both sides
/// wrote) and each thread's text under threads/. The program holds every row (NetworkThreads), keeps only rows whose
/// text is there, writes the texts and then the TSV into networklog/linkedin-inbox/ for the Function's next pass to log
/// beside the email threads, reads each back, and moves the mark forward and the floor back. A dry run checks the same and spools
/// nothing. The lane sends nothing, types nothing and writes no tracker row.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="tools">The CLI and the usage report.</param>
/// <param name="config">The settings.</param>
/// <param name="time">The clock.</param>
public sealed class NetworkLane(IShare share, NetworkTools tools, ServerConfig config, TimeProvider time)
{
    /// <summary>The TSV the model writes in its run folder.</summary>
    public const string Threads = "network-threads.tsv";

    /// <summary>The file the model writes in its run folder once the backlog has reached its window's end.</summary>
    public const string BacklogDone = "backlog-done.txt";

    /// <summary>The word the floor's file holds once the backlog is done.</summary>
    public const string Done = "done";

    /// <summary>Runs the lane and writes its spool and DONE note.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<LaneReport> RunAsync(LaneStart start, CancellationToken ct)
    {
        DateTimeOffset began = time.GetUtcNow();
        LaneConfig lane = config.For(Lane.Network);
        string folder = RunSlot.RunFolder(start.Day, Lane.Network, lane.Model, start.Dry, start.Slot);
        string runFolder = share.Full(folder);
        var lines = new List<string>(start.Lines);
        DateTimeOffset? mark = await TimeAsync(WritePolicy.HighWater, ct);
        string floorText = await share.ExistsAsync(WritePolicy.Floor, ct) ? (await share.ReadAsync(WritePolicy.Floor, ct)).Trim() : string.Empty;
        var bounds = new Bounds(mark, floorText == Done, NetworkThreads.When(floorText, out var f) ? f : null);
        await share.WriteAsync($"{folder}/high-water.txt", bounds.Text(), ct);
        var request = new ClaudeRequest(ClaudeArguments.For(lane, Lane.Network, share.Root, runFolder, start.Dry), runFolder, $"{runFolder}/stream.jsonl", TimeSpan.FromMinutes(lane.TimeoutMinutes));
        ClaudeRun run = await tools.Claude.RunAsync(request, ct);
        lines.Add($"claude -p exit {run.ExitCode}{(run.TimedOut ? ", killed at the wall-clock limit or by a stop" : string.Empty)}.");
        CancellationToken finish = CancellationToken.None;
        LaneResult? result = LaneResult.FromLine(run.ResultLine);
        if (result?.Rows.Any(row => row.Outcome == "sent") == true)
        {
            lines.Add("DEFECT: the network lane reported a send; it may send nothing. Read the page.");
        }

        string tsv = $"{folder}/{Threads}";
        var (rows, refused) = await share.ExistsAsync(tsv, finish) ? NetworkThreads.Parse(await share.ReadAsync(tsv, finish)) : ([], []);
        var dropped = refused.Select(why => new Dropped(tsv, why)).ToList();
        var kept = new List<(NetworkThread Row, string Text)>();
        foreach (NetworkThread row in rows)
        {
            string text = $"{folder}/threads/{row.ThreadId}.txt";
            if (await share.ExistsAsync(text, finish) && await share.ReadAsync(text, finish) is { Length: > 0 } body)
            {
                kept.Add((row, body));
            }
            else
            {
                dropped.Add(new Dropped(row.Person, $"no text at {text}"));
            }
        }
        lines.Add($"{rows.Count} two-way threads read from {tsv}, {kept.Count} with their text, {refused.Count} lines refused.");
        bool backlogDone = await share.ExistsAsync($"{folder}/{BacklogDone}", finish);
        await SpoolAsync(start, kept, bounds, backlogDone, lines, finish);
        await tools.Usage.WriteAsync(run, start, folder, began, lines, finish);
        var report = new LaneReport(Lane.Network, lane.Model, start.Dry, began, time.GetUtcNow(), start.Before, [], dropped, result, [], lines, null, run.TimedOut || result is null) { Queued = kept.Count };
        await share.WriteAsync($"{folder}/DONE.md", DoneNote.Render(report), finish);
        return report;
    }

    private async Task<DateTimeOffset?> TimeAsync(string path, CancellationToken ct) =>
        await share.ExistsAsync(path, ct) && NetworkThreads.When(await share.ReadAsync(path, ct), out var at) ? at : null;

    private async Task SpoolAsync(LaneStart start, IReadOnlyList<(NetworkThread Row, string Text)> kept, Bounds bounds, bool backlogDone, List<string> lines, CancellationToken ct)
    {
        if (start.Dry)
        {
            lines.Add("Dry run: nothing spooled and the mark not moved.");
            return;
        }
        if (kept.Count == 0)
        {
            lines.Add("No thread to spool; the mark stays where it was.");
            await FloorAsync([], bounds, backlogDone, lines, ct);
            return;
        }
        foreach (var (row, text) in kept)
        {
            string path = $"{WritePolicy.LinkedInText}{row.ThreadId}.txt";
            await share.WriteAsync(path, text, ct);
            if (await share.ReadAsync(path, ct) != text)
            {
                lines.Add($"DEFECT: {path} did not read back; nothing spooled.");
                return;
            }
        }
        string spool = $"{WritePolicy.LinkedInInbox}{RunSlot.Stamp(time.GetUtcNow())}-server6am.tsv";
        string body = NetworkThreads.Render(kept.Select(k => k.Row));
        await share.WriteNewAsync(spool, body, ct);
        if (await share.ReadAsync(spool, ct) != body)
        {
            lines.Add($"DEFECT: {spool} did not read back; the mark not moved.");
            return;
        }
        lines.Add($"Spooled and read back: {spool}, {kept.Count} threads, their texts under {WritePolicy.LinkedInText}.");
        if (NetworkThreads.HighWater(kept.Select(k => k.Row), bounds.Mark) is { } next && next != bounds.Mark)
        {
            await share.WriteAsync(WritePolicy.HighWater, NetworkThreads.Iso(next) + "\n", ct);
            lines.Add($"High-water mark moved to {NetworkThreads.Iso(next)}.");
        }
        await FloorAsync([.. kept.Select(k => k.Row)], bounds, backlogDone, lines, ct);
    }

    private async Task FloorAsync(IReadOnlyList<NetworkThread> rows, Bounds bounds, bool backlogDone, List<string> lines, CancellationToken ct)
    {
        if (bounds.BacklogDone)
        {
            return;
        }
        if (backlogDone)
        {
            await share.WriteAsync(WritePolicy.Floor, Done + "\n", ct);
            lines.Add("The backlog reached its window's end; the floor reads done.");
        }
        else if (NetworkThreads.Floor(rows, bounds.Floor) is { } floor && floor != bounds.Floor)
        {
            await share.WriteAsync(WritePolicy.Floor, NetworkThreads.Iso(floor) + "\n", ct);
            lines.Add($"Backlog floor moved to {NetworkThreads.Iso(floor)}.");
        }
    }

    /// <summary>Where the model reads to: down to the mark for the new, and on below the floor for the backlog until it is done.</summary>
    /// <param name="Mark">The newest exchange spooled so far, null before the first run.</param>
    /// <param name="BacklogDone">True once the backlog reached its window's end.</param>
    /// <param name="Floor">The oldest exchange the backlog reached, null before it began.</param>
    private sealed record Bounds(DateTimeOffset? Mark, bool BacklogDone, DateTimeOffset? Floor)
    {
        /// <summary>The run folder's high-water.txt.</summary>
        public string Text() =>
            $"mark: {(Mark is { } m ? NetworkThreads.Iso(m) : "none")}\nfloor: {(BacklogDone ? Done : Floor is { } f ? NetworkThreads.Iso(f) : "none")}\n";
    }
}
