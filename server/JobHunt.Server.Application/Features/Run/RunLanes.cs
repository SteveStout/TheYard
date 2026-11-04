namespace JobHunt.Server.Application.Features.Run;

/// <summary>What the command line asked of a run.</summary>
/// <param name="Dry">True for --dry-run.</param>
/// <param name="Only">The one lane --lane named, or null for all three.</param>
public sealed record RunOptions(bool Dry, Lane? Only)
{
    /// <summary>The options a command line asks for, or null when it holds anything this program does not know.</summary>
    /// <param name="arguments">The arguments, the verb first.</param>
    public static RunOptions? Parse(IReadOnlyList<string> arguments)
    {
        bool dry = false;
        Lane? only = null;
        for (int i = 1; i < arguments.Count; i++)
        {
            if (arguments[i] == "--dry-run")
            {
                dry = true;
            }
            else if (arguments[i] == "--lane" && i + 1 < arguments.Count && RunSlot.Parse(arguments[i + 1]) is { } lane)
            {
                only = lane;
                i++;
            }
            else
            {
                return null;
            }
        }
        return new RunOptions(dry, only);
    }
}

/// <summary>The checks that stand between the timer and a browser: the mount, the lock, the desktop and the clock.</summary>
/// <param name="Mount">The share's mount.</param>
/// <param name="Lock">The run lock.</param>
/// <param name="Desktop">The desktop.</param>
/// <param name="Time">The clock.</param>
public sealed record RunGuards(IMount Mount, IRunLock Lock, IDesktop Desktop, TimeProvider Time);

/// <summary>The three steps of a lane: what the morning reads, what it builds and what it runs.</summary>
/// <param name="Reader">The morning's reads.</param>
/// <param name="Builder">The worklists.</param>
/// <param name="Runner">The lanes.</param>
/// <param name="Recounter">The live invite count.</param>
public sealed record LaneWork(DayReader Reader, WorklistBuilder Builder, LaneRunner Runner, InviteRecount Recounter);

/// <summary>
/// The morning: mount, lock, registry, then the three lanes in order, then the card and the close row. LinkedIn fails
/// closed: a stop condition, a blocked row, a run that gave no result, or another lane LIVE ends the LinkedIn work of the
/// day, and that includes the apply lane's rows on linkedin.com, because they are a drive too. The registry is read
/// again before each lane. A LinkedIn stop is a file for the day, so a hard stop for the day stays one through any later
/// run. The invite room is cut from the live count in code, so the pause at 90 is the program's and
/// not only the model's. A stop from the service holds what has not started, and everything after it still closes.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="guards">The checks.</param>
/// <param name="work">The reads, the worklists and the lanes.</param>
/// <param name="config">The settings.</param>
public sealed class RunLanes(IShare share, RunGuards guards, LaneWork work, ServerConfig config)
{
    private const int RegistryTailBytes = 64 * 1024;
    private const string Spool = "inbox/append-inbox/state.md";

    /// <summary>The whole run. The answer is the line or card for the journal, and whether it stopped short.</summary>
    /// <param name="options">The command line.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<(int Exit, string Output)> ExecuteAsync(RunOptions options, CancellationToken ct)
    {
        DateTimeOffset started = guards.Time.GetUtcNow();
        DateOnly day = RunSlot.Day(started);
        bool dry = options.Dry || config.DryRun;
        if (await guards.Mount.FreshAsync(ct) is { } stale)
        {
            return (1, $"{RegistryRows.Lane} stopped before anything ran: {stale}");
        }
        await using IAsyncDisposable? held = await guards.Lock.TakeAsync(RunSlot.DayFolder(day), ct);
        if (held is null)
        {
            return (0, $"{RegistryRows.Lane}: another run holds the lock; this one did nothing.");
        }
        var lanes = RunSlot.Order.Where(lane => options.Only is null || options.Only == lane).Where(lane => config.For(lane).Enabled).ToList();
        try
        {
            await share.WriteNewAsync(RegistryRows.Path(started, "registry"), RegistryRows.Live(started, dry, string.Join(", ", lanes.Select(RunSlot.Word))), ct);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (1, $"{RegistryRows.Lane} stopped before anything ran: the registry row would not write: {failure.Message}");
        }

        var reports = new List<LaneReport>();
        string? broke = null;
        try
        {
            await WorkAsync(lanes, day, dry, reports, ct);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            broke = $"DEFECT: the run stopped early: {failure.Message}. Read the DONE notes before any lane runs again.";
        }
        return (broke is null ? 0 : 1, await CloseAsync(new CloseOf(reports, started, day, dry, broke, reports.Count < RunSlot.Order.Count), CancellationToken.None));
    }

    private async Task WorkAsync(IReadOnlyList<Lane> lanes, DateOnly day, bool dry, List<LaneReport> reports, CancellationToken ct)
    {
        DayReads? reads = null;
        Ceilings? ceilings = null;
        string? closed = null;
        try
        {
            reads = await work.Reader.ReadAsync(day, ct);
            ceilings = Ceilings.Read(reads.Limits, reads.Conventions);
        }
        catch (Exception failure) when (failure is FormatException or IOException)
        {
            closed = $"a gate could not be read: {failure.Message}";
        }
        closed ??= await guards.Desktop.EnsureChromeAsync(ct);
        string? linkedInStop = await share.ExistsAsync(RunSlot.LinkedInClosed(day), ct) ? (await share.ReadAsync(RunSlot.LinkedInClosed(day), ct)).Trim() : null;
        foreach (Lane lane in lanes)
        {
            if (await AlreadyRanAsync(lane, day, dry, ct))
            {
                continue;
            }
            OutreachCounts? before = reads is null ? null : OutreachCounts.From(reads.Rows, day);
            var start = new LaneStart(lane, day, dry, before, [$"Lane doc nightly/{RunSlot.Doc(lane)}, the tracker, outreach-limits.md, CONVENTIONS.md, systemRules.md and responders.md read this run."]);
            string? linkedIn = linkedInStop ?? await SecondDriverAsync(ct);
            string? held = closed
                ?? (ct.IsCancellationRequested ? "the service was stopped" : null)
                ?? (RunSlot.OnLinkedIn(lane) ? linkedIn : null)
                ?? await work.Runner.CrashedAsync(start, ct);
            if (held is not null)
            {
                reports.Add(await work.Runner.HoldAsync(start, held, CancellationToken.None));
                continue;
            }
            int invitesRoom = 0;
            if (lane == Lane.Invites && before!.InviteRoom(ceilings!) > 0)
            {
                Recount recount = await work.Recounter.ReadAsync(start, ceilings!, before.InviteRoom(ceilings!), ct);
                if (recount.Held is not null)
                {
                    linkedInStop ??= await CloseLinkedInAsync(day, recount.Stop, dry, ct);
                    reports.Add(await work.Runner.HoldAsync(start with { Lines = [.. start.Lines, .. recount.Lines] }, recount.Held, CancellationToken.None));
                    continue;
                }
                invitesRoom = recount.Room;
                start = start with { Lines = [.. start.Lines, .. recount.Lines], LiveCount = recount.Live };
            }
            Worklist list = await WorklistAsync(lane, reads!, ceilings!, before!, reports, invitesRoom, linkedIn, dry, ct);
            LaneReport report = await work.Runner.RunAsync(start, list, ct);
            reports.Add(report);
            if ((RunSlot.OnLinkedIn(lane) && report.ClosesLinkedIn) || report.Answer?.StopsLinkedIn == true)
            {
                linkedInStop ??= await CloseLinkedInAsync(day, $"{RunSlot.Word(lane)} lane did not finish clean, read its DONE note", dry, CancellationToken.None);
            }
        }
    }

    private async Task<string?> CloseLinkedInAsync(DateOnly day, string? stop, bool dry, CancellationToken ct)
    {
        if (stop is not null && !dry)
        {
            await share.WriteAsync(RunSlot.LinkedInClosed(day), stop + "\n", ct);
        }
        return stop;
    }

    private async Task<bool> AlreadyRanAsync(Lane lane, DateOnly day, bool dry, CancellationToken ct)
    {
        string note = $"{RunSlot.RunFolder(day, lane, config.For(lane).Model, false)}/DONE.md";
        return !dry && await share.ExistsAsync(note, ct) && !(await share.ReadAsync(note, ct)).Contains("\nHELD: ", StringComparison.Ordinal);
    }

    private async Task<string?> SecondDriverAsync(CancellationToken ct)
    {
        string tail = await share.TailAsync("state.md", RegistryTailBytes, ct);
        foreach (ShareFile file in await share.ListAsync(Spool, "*.md", ct))
        {
            try
            {
                tail += RegistryTail.Stamped(Path.GetFileNameWithoutExtension(file.Path), await share.ReadAsync(file.Path, ct));
            }
            catch (FileNotFoundException)
            {
                // The merger took it between the list and the read, so the fragment is in state.md now.
            }
        }
        var live = RegistryTail.LiveOnLinkedIn(tail, guards.Time.GetUtcNow());
        return live.Count > 0 ? $"{string.Join(", ", live)} LIVE on LinkedIn" : null;
    }

    private Task<Worklist> WorklistAsync(Lane lane, DayReads reads, Ceilings ceilings, OutreachCounts before, IReadOnlyList<LaneReport> reports, int invitesRoom, string? linkedIn, bool dry, CancellationToken ct)
    {
        int invitesSent = reports.Where(report => report.Lane == Lane.Invites).Sum(report => report.Sends.Count);
        return lane switch
        {
            Lane.Invites => work.Builder.InvitesAsync(reads, invitesRoom, dry, ct),
            Lane.Messages => work.Builder.MessagesAsync(reads, before.MessageRoom(ceilings, invitesSent), dry, ct),
            _ => work.Builder.ApplyAsync(reads, linkedIn, dry, ct),
        };
    }

    private async Task<string> CloseAsync(CloseOf run, CancellationToken ct)
    {
        (IReadOnlyList<LaneReport> reports, DateTimeOffset started, DateOnly day, _, string? broke, bool oneLane) = run;
        string card = $"{RegistryRows.Lane}: the card could not be built.\n";
        try
        {
            card = await CardAsync(run, ct);
            string path = $"{RunSlot.DayFolder(day)}/{RunSlot.Hour}-card.md";
            if ((reports.Count > 0 || broke is not null) && !(oneLane && await share.ExistsAsync(path, ct)))
            {
                await share.WriteAsync(path, card, ct);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            card += $"The card was not written: {failure.Message}\n";
        }
        try
        {
            DateTimeOffset now = guards.Time.GetUtcNow();
            await share.WriteNewAsync(RegistryRows.Path(now, "close"), RegistryRows.Closed(now, started, card), ct);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            card += $"The close row was not written: {failure.Message}\n";
        }
        return card;
    }

    private async Task<string> CardAsync(CloseOf run, CancellationToken ct)
    {
        (IReadOnlyList<LaneReport> reports, _, DateOnly day, bool dry, string? broke, _) = run;
        foreach (string line in reports.Select(report => CleanRuns.Line(day, report)).OfType<string>())
        {
            await share.AppendAsync(WritePolicy.RunsTable, line, ct);
        }
        string table = await share.ExistsAsync(WritePolicy.RunsTable, ct) ? await share.ReadAsync(WritePolicy.RunsTable, ct) : string.Empty;
        var extra = new List<string>();
        if (broke is not null)
        {
            extra.Add(broke);
        }
        extra.AddRange(reports.SelectMany(report => report.Lines).Where(line => line.StartsWith("TRACKER ROW NOT WRITTEN", StringComparison.Ordinal) || line.StartsWith("DEFECT", StringComparison.Ordinal)));
        foreach (LaneReport report in reports)
        {
            string target = config.For(report.Lane).TargetModel;
            if (target.Length > 0 && CleanRuns.Eligible(table, report.Lane, report.Model))
            {
                extra.Add($"{RunSlot.Word(report.Lane)}: {CleanRuns.Needed} clean runs, eligible for {target} (the local config's Model line).");
            }
        }
        return RunCard.Render(day, dry, reports, extra);
    }

    private sealed record CloseOf(IReadOnlyList<LaneReport> Reports, DateTimeOffset Started, DateOnly Day, bool Dry, string? Broke, bool OneLane);
}
