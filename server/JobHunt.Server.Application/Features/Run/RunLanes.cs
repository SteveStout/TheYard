namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The morning: mount, lock, registry, then the lanes in order, the feed first and the queues read again after it so the
/// invites lane works what the feed just wrote, the network lane after the messages lane, then the card and the close
/// row. The network lane reads LinkedIn Messaging and sends nothing, but it is a drive: a second driver or the day's stop
/// file holds it like a sending lane, and only a hard stop it met closes LinkedIn for the day. The feed reads LinkedIn and sends
/// nothing: it waits on the day's LinkedIn stop file, never on another driver, and only a hard stop it met on the page
/// (a checkpoint, a CAPTCHA, a sign-in, a restriction, the weekly limit) closes LinkedIn for the day. LinkedIn fails
/// closed: a stop condition, a blocked row, a run that gave no result, or another lane LIVE ends the LinkedIn work of the
/// day, and that includes the apply lane's rows on linkedin.com, because they are a drive too. The registry is read
/// again before each lane. A LinkedIn stop is a file for the day, so a hard stop for the day stays one through any later
/// run. The invite room is cut from the live count in code, so the pause at 90 is the program's and
/// not only the model's. A stop from the service holds what has not started, and everything after it still closes.
/// A slot run works one caught role the same way under the same lock, gates and day's stop file: the feed for that role,
/// then the invites lane on that role's queue only, then the apply lane on that role's posting only, its folders and card
/// named for its slot. It never starts from 05:30 to 07:00 Central, the morning's own window, nor so close before 05:30
/// that its lanes' limits could carry it past then, and that is read again once the lock is held, because the wait for
/// it counts too. A slot whose lane already ran live does not run that lane again, and a hand-off must name the slot's
/// own posting.
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
        string quiet = $"{RegistryRows.Lane}: no slot run starts from 05:30 to 07:00 Central, nor where its lanes could run past 05:30; {options.Slot} did nothing.";
        if (options.Role is not null && RunSlot.Quiet(started, RoleLead(config)))
        {
            return (0, quiet);
        }
        if (await guards.Mount.FreshAsync(ct) is { } stale)
        {
            return (1, $"{RegistryRows.Lane} stopped before anything ran: {stale}");
        }
        RoleHandoff? role = options.Role is { } handoff && await share.ExistsAsync(handoff, ct) ? RoleHandoff.Parse(handoff, await share.ReadAsync(handoff, ct)) : null;
        if (options.Role is not null && role is null)
        {
            return (1, $"{RegistryRows.Lane} stopped before anything ran: the hand-off {options.Role} would not read");
        }
        if (role is not null && options.Slot != RunSlot.Pounce + role.Key)
        {
            return (1, $"{RegistryRows.Lane} stopped before anything ran: the hand-off {options.Role} names posting {role.Key}, not slot {options.Slot}");
        }
        await using IAsyncDisposable? held = await guards.Lock.TakeAsync(RunSlot.DayFolder(day), ct);
        if (held is null)
        {
            return (0, $"{RegistryRows.Lane}: another run holds the lock; this one did nothing.");
        }
        if (role is not null && RunSlot.Quiet(guards.Time.GetUtcNow(), RoleLead(config)))
        {
            return (0, quiet);
        }
        var lanes = (role is null ? RunSlot.Order : RunSlot.RoleOrder).Where(lane => options.Only is null || options.Only == lane).Where(lane => config.For(lane).Enabled).ToList();
        try
        {
            string words = string.Join(", ", lanes.Select(RunSlot.Word)) + (role is null ? string.Empty : $" for slot {options.Slot}");
            await share.WriteNewAsync(RegistryRows.Path(started, "registry"), RegistryRows.Live(started, dry, words), ct);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (1, $"{RegistryRows.Lane} stopped before anything ran: the registry row would not write: {failure.Message}");
        }

        var reports = new List<LaneReport>();
        string? broke = null;
        try
        {
            await WorkAsync(lanes, new LaneStart(Lane.Feed, day, dry, null, [], Slot: options.Slot, Role: role), reports, ct);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            broke = $"DEFECT: the run stopped early: {failure.Message}. Read the DONE notes before any lane runs again.";
        }
        return (broke is null ? 0 : 1, await CloseAsync(new CloseOf(reports, started, day, dry, broke, options.Only is not null || reports.Count < lanes.Count, options.Slot), CancellationToken.None));
    }

    private async Task WorkAsync(IReadOnlyList<Lane> lanes, LaneStart run, List<LaneReport> reports, CancellationToken ct)
    {
        (DateOnly day, bool dry) = (run.Day, run.Dry);
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
            if (await AlreadyRanAsync(lane, run, ct))
            {
                continue;
            }
            OutreachCounts? before = reads is null ? null : OutreachCounts.From(reads.Rows, day);
            var start = run with { Lane = lane, Before = before, Lines = [$"Lane doc nightly/{RunSlot.Doc(lane)}, the tracker, outreach-limits.md, CONVENTIONS.md, systemRules.md, responders.md and BLACKLIST.md read this run."] };
            string? linkedIn = linkedInStop ?? await SecondDriverAsync(ct);
            string? held = closed
                ?? (ct.IsCancellationRequested ? "the service was stopped" : null)
                ?? (RunSlot.OnLinkedIn(lane) || lane == Lane.Network ? linkedIn : lane == Lane.Feed ? linkedInStop : null)
                ?? await work.Runner.CrashedAsync(start, ct);
            if (held is not null)
            {
                reports.Add(await work.Runner.HoldAsync(start, held, CancellationToken.None));
                continue;
            }
            if (lane == Lane.Feed)
            {
                LaneReport fed = await work.Feed.RunAsync(start, reads!, ct);
                reports.Add(fed);
                if (fed.Answer is { StopsLinkedIn: true } stopped)
                {
                    linkedInStop ??= await CloseLinkedInAsync(day, $"the feed lane met {stopped.StopCondition}: {stopped.StopDetail}", dry, CancellationToken.None);
                }
                reads = await work.Reader.ReadAsync(day, ct);
                continue;
            }
            if (lane == Lane.Network)
            {
                LaneReport read = await work.Network.RunAsync(start, reads!, ct);
                reports.Add(read);
                if (read.Answer is { StopsLinkedIn: true } stopped)
                {
                    linkedInStop ??= await CloseLinkedInAsync(day, $"the network lane met {stopped.StopCondition}: {stopped.StopDetail}", dry, CancellationToken.None);
                }
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
            DayReads worked = run.Role is null ? reads! : reads! with { Queues = await RoleQueueAsync(reads!, run, ct), Slot = run.Slot, Role = run.Role };
            Worklist list = await WorklistAsync(lane, worked, ceilings!, before!, reports, invitesRoom, linkedIn, dry, ct);
            LaneReport report = await work.Runner.RunAsync(start, list, ct);
            reports.Add(report);
            if ((RunSlot.OnLinkedIn(lane) && report.ClosesLinkedIn) || report.Answer?.StopsLinkedIn == true)
            {
                linkedInStop ??= await CloseLinkedInAsync(day, $"{RunSlot.Word(lane)} lane did not finish clean, read its DONE note", dry, CancellationToken.None);
            }
        }
    }

    /// <summary>The longest a role run can last: its lanes' limits, the recount's and five for the records; the Function's hold starts this long before 05:30.</summary>
    public static TimeSpan RoleLead(ServerConfig config) =>
        TimeSpan.FromMinutes(RunSlot.RoleOrder.Where(lane => config.For(lane).Enabled).Sum(lane => config.For(lane).TimeoutMinutes) + InviteRecount.Minutes + 5);

    private async Task<string?> CloseLinkedInAsync(DateOnly day, string? stop, bool dry, CancellationToken ct)
    {
        if (stop is not null && !dry)
        {
            await share.WriteAsync(RunSlot.LinkedInClosed(day), stop + "\n", ct);
        }
        return stop;
    }

    private async Task<bool> AlreadyRanAsync(Lane lane, LaneStart run, CancellationToken ct)
    {
        string note = $"{RunSlot.RunFolder(run.Day, lane, config.For(lane).Model, false, run.Slot)}/DONE.md";
        return !run.Dry && await share.ExistsAsync(note, ct) && !(await share.ReadAsync(note, ct)).Contains("\nHELD: ", StringComparison.Ordinal);
    }

    /// <summary>
    /// The open blocks of the caught role's own queue, the only blocks a slot run's invites lane works: read with the rest
    /// on a live run, and from the dry feed's run folder on a dry one, where the dry feed wrote it.
    /// </summary>
    private async Task<IReadOnlyList<QueueBlock>> RoleQueueAsync(DayReads reads, LaneStart run, CancellationToken ct)
    {
        string name = FeedLane.QueueName(run.Day, run.Role);
        if (!run.Dry)
        {
            return [.. reads.Queues.Where(block => block.Source.StartsWith(name + ":", StringComparison.Ordinal))];
        }
        string path = $"{RunSlot.RunFolder(run.Day, Lane.Feed, config.For(Lane.Feed).Model, true, run.Slot)}/{name}";
        return await share.ExistsAsync(path, ct) ? QueueBlocks.Open(name, await share.ReadAsync(path, ct), []) : [];
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
        var holders = RegistryTail.Holders(tail, guards.Time.GetUtcNow());
        return holders.Count > 0 ? $"a hold on LinkedIn or Chrome by {string.Join(", ", holders)}" : null;
    }

    private Task<Worklist> WorklistAsync(Lane lane, DayReads reads, Ceilings ceilings, OutreachCounts before, IReadOnlyList<LaneReport> reports, int invitesRoom, string? linkedIn, bool dry, CancellationToken ct)
    {
        int invitesSent = reports.Where(report => report.Lane == Lane.Invites).Sum(report => report.Sends.Count);
        return lane switch
        {
            Lane.Invites => work.Links is { } links ? links.ApplyAsync(reads, work.Builder.InvitesAsync(reads, invitesRoom, dry, ct), ct) : work.Builder.InvitesAsync(reads, invitesRoom, dry, ct),
            Lane.Messages => work.Builder.MessagesAsync(reads, before.MessageRoom(ceilings, invitesSent), dry, ct),
            _ => work.Builder.ApplyAsync(reads, linkedIn, dry, ct),
        };
    }

    private async Task<string> CloseAsync(CloseOf run, CancellationToken ct)
    {
        (IReadOnlyList<LaneReport> reports, DateTimeOffset started, DateOnly day, _, string? broke, bool oneLane, string slot) = run;
        string card = $"{RegistryRows.Lane}: the card could not be built.\n";
        try
        {
            card = await CardAsync(run, ct);
            string path = RunSlot.Card(day, slot);
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
        (IReadOnlyList<LaneReport> reports, _, DateOnly day, bool dry, string? broke, _, _) = run;
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

    private sealed record CloseOf(IReadOnlyList<LaneReport> Reports, DateTimeOffset Started, DateOnly Day, bool Dry, string? Broke, bool OneLane, string Slot);
}
