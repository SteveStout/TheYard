namespace JobHunt.Server.Application.Features.Run;

/// <summary>What the comment line works with beside the share.</summary>
/// <param name="Claude">The CLI.</param>
/// <param name="Minter">make_ref.py.</param>
/// <param name="Prose">check_aiisms.py.</param>
/// <param name="Usage">The usage report.</param>
/// <param name="Roll">The random source the pick is rolled from; the morning's is the system's.</param>
public sealed record CommentTools(IClaudeCli Claude, ICodeMinter Minter, IProseCheck Prose, UsageReport Usage, Random Roll);

/// <summary>
/// Runs the comment line, right after the feed (CONVENTIONS 20261119-141000Z, the one scoped exception to the never-type
/// rule: editing the text of Steve's OWN comments to add one line). A read run lists his comments of the window in
/// comments.json and touches nothing. The program holds each to the skip rules, rolls about one in three of the rest
/// (<see cref="CommentPicks"/>), mints one code each with make_ref.py, writes the line, proofreads it with check_aiisms.py,
/// and, on a live run whose read ended clean, hands the edits to an edit run. Each edit counts only when the comment's text
/// read after a reload holds his words as they were, the line last and the code once. A dry run reads seven days, mints
/// nothing, and lists the picks and the skips. The lane writes no tracker row and sends nothing.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="tools">The CLI, the minter, the proofread, the usage report and the roll.</param>
/// <param name="config">The settings.</param>
/// <param name="time">The clock.</param>
public sealed class CommentLine(IShare share, CommentTools tools, ServerConfig config, TimeProvider time)
{
    private const string DryCode = "[code]";

    /// <summary>Runs the lane and writes its DONE note.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<LaneReport> RunAsync(LaneStart start, CancellationToken ct)
    {
        DateTimeOffset began = time.GetUtcNow();
        LaneConfig lane = config.For(Lane.Commentline);
        string folder = RunSlot.RunFolder(start.Day, Lane.Commentline, lane.Model, start.Dry, start.Slot);
        string runFolder = share.Full(folder);
        var lines = new List<string>(start.Lines);
        var timeout = TimeSpan.FromMinutes(lane.TimeoutMinutes);
        ClaudeRun read = await tools.Claude.RunAsync(new ClaudeRequest(ClaudeArguments.For(lane, Lane.Commentline, share.Root, runFolder, start.Dry), runFolder, $"{runFolder}/stream.jsonl", timeout), ct);
        lines.Add($"Read run: claude -p exit {read.ExitCode}{(read.TimedOut ? ", killed at the wall-clock limit or by a stop" : string.Empty)}.");
        CancellationToken finish = CancellationToken.None;
        await tools.Usage.WriteAsync(read, start, folder, began, lines, finish);
        LaneResult? answer = LaneResult.FromLine(read.ResultLine);
        bool cutShort = read.TimedOut || answer is null;
        bool live = !start.Dry && !cutShort && answer!.StopCondition == "none";

        string readPath = $"{folder}/{CommentPicks.ReadFile}";
        IReadOnlyList<OwnComment> comments = await share.ExistsAsync(readPath, finish) ? CommentPicks.Parse(await share.ReadAsync(readPath, finish)) : [];
        int hours = CommentPicks.Window(start.Dry);
        CommentRoll roll = CommentPicks.Roll(comments, hours, tools.Roll);
        lines.Add($"{comments.Count} comments read from {readPath} for the last {hours} hours; {roll.Eligible} passed the skip rules; the roll picked {roll.Picked.Count} (cap {CommentPicks.Cap}).");
        var dropped = new List<Dropped>(roll.Dropped);
        IReadOnlyList<CommentEdit> edits = await LinesAsync(roll.Picked, live, dropped, finish);
        lines.AddRange(edits.Select(edit => $"Pick {edit.Row}: {edit.Comment.PostAuthor}, {edit.Comment.Topic}, code {edit.Code}: {edit.Line}"));
        await share.WriteAsync($"{folder}/{CommentPicks.EditFile}", CommentPicks.Render(edits), finish);

        int edited = 0;
        if (start.Dry)
        {
            lines.Add("Dry run: no code minted, nothing edited; the picks above are what a live run would have edited.");
        }
        else if (!live)
        {
            lines.Add("The read run did not end clean: no code minted, no edit run.");
        }
        else if (edits.Count == 0)
        {
            lines.Add("Nothing picked; no edit run.");
        }
        else
        {
            DateTimeOffset editBegan = time.GetUtcNow();
            ClaudeRun edit = await tools.Claude.RunAsync(new ClaudeRequest(ClaudeArguments.CommentEdit(lane, share.Root, runFolder), runFolder, $"{runFolder}/stream-edit.jsonl", timeout), ct);
            lines.Add($"Edit run: claude -p exit {edit.ExitCode}{(edit.TimedOut ? ", killed at the wall-clock limit or by a stop" : string.Empty)}.");
            answer = LaneResult.FromLine(edit.ResultLine);
            cutShort = edit.TimedOut || answer is null;
            edited = Verify(edits, answer, lines);
            await tools.Usage.WriteAsync(edit, start, folder, editBegan, lines, finish);
        }
        var report = new LaneReport(Lane.Commentline, lane.Model, start.Dry, began, time.GetUtcNow(), start.Before, [], dropped, answer, [], lines, null, cutShort)
        {
            Queued = start.Dry || live ? edits.Count : 0,
            Edited = edited,
        };
        await share.WriteAsync($"{folder}/DONE.md", DoneNote.Render(report), finish);
        return report;
    }

    private static int Verify(IReadOnlyList<CommentEdit> edits, LaneResult? answer, List<string> lines)
    {
        int edited = 0;
        foreach (CommentEdit edit in edits)
        {
            RowResult? row = answer?.Rows.FirstOrDefault(result => result.Row == edit.Row);
            string who = $"Edit {edit.Row}: {edit.Comment.PostAuthor}, {edit.Comment.Topic}, code {edit.Code}";
            if (row?.Outcome != "sent")
            {
                lines.Add($"{who}: {row?.Outcome ?? "no result"}{(row?.Reason is { Length: > 0 } reason ? ", " + reason : string.Empty)}.");
                continue;
            }
            IReadOnlyList<string> missing = CommentPicks.Missing(edit, row.Evidence?.ReadBack);
            if (missing.Count == 0)
            {
                edited++;
                lines.Add($"{who}: edited, verified on the reload.");
            }
            else
            {
                lines.Add($"DEFECT: {who}: claimed edited, UNVERIFIED: {string.Join("; ", missing)}. Read his comment on {edit.Comment.PostAuthor}'s post.");
            }
        }
        foreach (RowResult extra in answer?.Rows.Where(result => result.Outcome == "sent" && edits.All(edit => edit.Row != result.Row)) ?? [])
        {
            lines.Add($"DEFECT: the edit run claims row {extra.Row}, which the worklist never held. Read his activity page.");
        }
        return edited;
    }

    private async Task<IReadOnlyList<CommentEdit>> LinesAsync(IReadOnlyList<OwnComment> picked, bool mint, List<Dropped> dropped, CancellationToken ct)
    {
        var edits = new List<CommentEdit>();
        foreach (OwnComment comment in picked)
        {
            string preview = CommentPicks.Line(comment.Tie!, DryCode);
            if (!await tools.Prose.CleanAsync(preview, ct))
            {
                dropped.Add(new Dropped(CommentPicks.Who(comment), "the line failed check_aiisms.py"));
                continue;
            }
            string? code = !mint ? DryCode : await tools.Minter.MintAsync(comment.PostAuthor ?? string.Empty, comment.Company ?? string.Empty, $"comment: {comment.Topic}", RunSlot.Word(Lane.Commentline), ct);
            if (code is null)
            {
                dropped.Add(new Dropped(CommentPicks.Who(comment), "make_ref.py failed, no code, no edit"));
                continue;
            }
            edits.Add(new CommentEdit(edits.Count + 1, comment, code, CommentPicks.Line(comment.Tie!, code)));
        }
        return edits;
    }
}
