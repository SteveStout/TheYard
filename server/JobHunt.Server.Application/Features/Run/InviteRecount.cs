namespace JobHunt.Server.Application.Features.Run;

/// <summary>What the recount came to.</summary>
/// <param name="Live">The count from the sent page, null when it was not read.</param>
/// <param name="Room">The invites the program will put on the worklist.</param>
/// <param name="Held">Why no invite goes today, or null.</param>
/// <param name="Stop">Why LinkedIn itself closed, or null.</param>
/// <param name="Lines">Lines for the DONE note.</param>
public sealed record Recount(int? Live, int Room, string? Held, string? Stop, IReadOnlyList<string> Lines);

/// <summary>
/// The live invite count, read by a short model run before the worklist is cut, so the pause at 90 and the stop at 89 are
/// the program's arithmetic and not only the lane doc's. The sent page governs: the tracker has read eight low against
/// it. A live run with no count sends no invite, and a run that gave no answer closes LinkedIn, because it may have ended
/// on a checkpoint. A dry run cannot scroll, so a count it did not read is no defect.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="claude">The CLI.</param>
/// <param name="config">The settings.</param>
public sealed class InviteRecount(IShare share, IClaudeCli claude, ServerConfig config)
{
    private const int Minutes = 8;

    /// <summary>Reads the count and cuts the room.</summary>
    /// <param name="start">The lane.</param>
    /// <param name="ceilings">The ceilings.</param>
    /// <param name="room">The room the tracker's counts leave.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<Recount> ReadAsync(LaneStart start, Ceilings ceilings, int room, CancellationToken ct)
    {
        LaneConfig lane = config.For(start.Lane);
        string runFolder = share.Full(RunSlot.RunFolder(start.Day, start.Lane, lane.Model, start.Dry, start.Slot));
        var request = new ClaudeRequest(ClaudeArguments.Recount(lane, share.Root, runFolder, start.Dry), runFolder, $"{runFolder}/recount.jsonl", TimeSpan.FromMinutes(Minutes));
        ClaudeRun run = await claude.RunAsync(request, ct);
        LaneResult? result = LaneResult.FromLine(run.ResultLine);
        var lines = new List<string> { $"Recount: {(result?.LiveInviteCount is { } read ? read.ToString(System.Globalization.CultureInfo.InvariantCulture) : "not read")}. {result?.StopDetail}" };
        if (result is null)
        {
            return new Recount(null, 0, "the recount gave no result", "the recount gave no result", lines);
        }
        if (result.StopCondition != "none")
        {
            string stop = $"stop condition {result.StopCondition} on the recount";
            return new Recount(null, 0, stop, stop, lines);
        }
        if (result.LiveInviteCount is not { } live)
        {
            return start.Dry ? new Recount(null, room, null, null, lines) : new Recount(null, 0, "no recount, no invite", null, lines);
        }
        return live >= ceilings.InvitePause
            ? new Recount(live, 0, $"invite pause, {live} on the sent page", null, lines)
            : new Recount(live, Math.Min(room, ceilings.InvitePause - 1 - live), null, null, lines);
    }
}
