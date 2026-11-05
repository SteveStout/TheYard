using System.Text.RegularExpressions;

namespace JobHunt.Application.Features.NetworkLog;

/// <summary>Which way the switch is set: off runs nothing, shadow writes what it would draft, on drafts.</summary>
public enum NetworkMode
{
    /// <summary>Absent or anything else: nothing runs.</summary>
    Off,

    /// <summary>Reads, logs and writes would-drafts to the share; the draft port is never called.</summary>
    Shadow,

    /// <summary>Reads, logs and writes Gmail drafts under the Ready label.</summary>
    On,
}

/// <summary>What one pass did, for its line, its row and the run line.</summary>
/// <param name="Line">The pass line as written to the share.</param>
/// <param name="Counts">The counts for network.Passes.</param>
/// <param name="Cut">Threads left unread because the pass ran out of time.</param>
public sealed record NetworkPassResult(string Line, PassCounts Counts, int Cut);

/// <summary>
/// The network log's daily pass: the window of Gmail threads less the campaign's, each two-way thread into its row,
/// the stop and quiet rules, the LinkedIn spool, the TopicLine export, on a Monday the week's due follow-ups, and one
/// line on the share. In shadow the draft port is the refusing one, so nothing reaches Gmail but reads.
/// </summary>
public sealed partial class NetworkLogPass(IConversationStore rows, IThreadReader gmail, IDraftWriter drafts, IMergeStore share, TimeProvider time)
{
    /// <summary>The blog's feed, read on a Monday when a due row names a post; with none set no draft names one.</summary>
    public IBlogPosts Posts { get; init; } = NoPosts.Instance;

    /// <summary>The daily window in days.</summary>
    public const int DailyDays = 2;

    /// <summary>The first pass's window in days.</summary>
    public const int BackfillDays = 120;

    /// <summary>The most threads one pass reads.</summary>
    public const int MaxThreads = 1500;

    /// <summary>The time a pass may spend reading threads before it stops and says how many it left.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(7);

    /// <summary>Runs one pass and answers its line; the caller writes the Passes row around it.</summary>
    /// <param name="mode">Shadow or on.</param>
    /// <param name="campaignThreads">The campaign's open threads, which this pass leaves alone.</param>
    /// <param name="ct">The invocation's cancellation.</param>
    public async Task<NetworkPassResult> RunAsync(NetworkMode mode, IReadOnlySet<string> campaignThreads, CancellationToken ct)
    {
        long started = time.GetTimestamp();
        var now = time.GetUtcNow();
        bool first = (await rows.StartsAsync(ct)).Count == 0;
        var ids = (await gmail.FindThreadIdsAsync(new ThreadSearch(first ? BackfillDays : DailyDays, MaxThreads), ct)).Where(id => !campaignThreads.Contains(id)).ToList();
        var known = WarmContactRule.Known(await TextAsync("Job_Hunt_Tracker.csv", ct), await TextAsync("responders.md", ct));
        var tally = new Tally();
        var summaries = new List<ThreadSummary>();
        foreach (var chunk in ids.Chunk(4))
        {
            if (time.GetElapsedTime(started) > Budget)
            {
                tally.Cut = ids.Count - summaries.Count;
                break;
            }
            summaries.AddRange(await Task.WhenAll(chunk.Select(id => ReadAsync(id, ct))));
        }
        foreach (var thread in summaries)
        {
            await TakeAsync(thread, known, now, tally, ct);
        }
        tally.LinkedIn = await IngestLinkedInAsync(now, tally, ct);
        var topics = await new TopicImport(rows, share).RunAsync(DateOnly.FromDateTime(CentralClock.Local(now)), ct);
        if (topics.Refused.Count > 0)
        {
            await share.EnsureFolderAsync(NetworkFiles.Folder, ct);
            await share.AppendAsync(NetworkFiles.TopicsRefused, TrackerCsvText.Encode(string.Concat(topics.Refused.Select(r => $"{CentralClock.Local(now):yyyy-MM-dd} {r}\n"))), ct);
        }
        var open = await rows.OpenAsync(ct);
        if (mode == NetworkMode.On)
        {
            // Only `on` ever drafted, so only `on` has a draft to look for; shadow never touches the draft port.
            await QuietAsync(open.Where(r => r.State == ConversationState.Drafted && r.DraftChannel == Channel.Email && r.ThreadIds.Count > 0), now, tally, ct);
        }
        foreach (var row in open.Where(r => DueRule.IsStale(r, now)))
        {
            await rows.SetStateAsync(row.Id, ConversationState.Closed, "stale", ct);
            tally.Stopped++;
        }
        open = await rows.OpenAsync(ct);
        await share.EnsureFolderAsync(NetworkFiles.Folder, ct);
        await share.WriteAsync(NetworkFiles.TopicsNeeded, TrackerCsvText.Encode(NetworkFiles.TopicsNeededText(open, now)), ct);
        var local = CentralClock.Local(now);
        if (local.DayOfWeek == DayOfWeek.Monday)
        {
            var monday = await new MondayDrafts(rows, gmail, drafts, share, Posts).RunAsync(mode, DateOnly.FromDateTime(local), open, now, ct);
            (tally.Due, tally.Drafted, tally.RenderFailed, tally.Posts) = (monday.Due, monday.Drafted, monday.RenderFailed, monday.Posts);
        }
        var week = (await rows.StartsAsync(ct)).Count(at => IsoWeek.Of(DateOnly.FromDateTime(CentralClock.Local(at))) == IsoWeek.Of(DateOnly.FromDateTime(local)));
        int seconds = (int)time.GetElapsedTime(started).TotalSeconds;
        var extra = new List<(string, int)> { ("topics", topics.Applied), ("linkedin", tally.LinkedIn), ("backfill", first ? 1 : 0) };
        extra.Add(("render_failed", tally.RenderFailed));
        extra.Add(("posts", topics.Posts));
        extra.Add(("named", tally.Posts));
        if (tally.Cut > 0)
        {
            extra.Add(("cut", tally.Cut));
        }
        string line = NetworkFiles.Line(CentralClock.Local(time.GetUtcNow()), new PassLine(mode == NetworkMode.On ? "on" : "shadow", summaries.Count, tally.Upserted, tally.Stopped, tally.Quiet, tally.Personal, tally.Due, tally.Drafted, seconds, week, extra));
        var counts = new PassCounts(summaries.Count, tally.Upserted, tally.Stopped, tally.Quiet, tally.Drafted, week, seconds, line);
        return new NetworkPassResult(line, counts, tally.Cut);
    }

    private async Task<ThreadSummary> ReadAsync(string id, CancellationToken ct)
    {
        var thread = await gmail.ReadSummaryAsync(id, ct);
        var people = thread.Messages.Where(m => !m.Outbound && HumanSenderRule.IsHuman(m.From)).ToList();
        if (people.Count == 1 && !thread.Messages.Any(m => m.Outbound || HumanSenderRule.IsSteve(m.From)) && Invitation().IsMatch(thread.Subject + " " + people[0].Snippet)
            && await gmail.ReadHasCalendarPartAsync(id, ct))
        {
            // Only a person's lone invite is read in full, for its calendar part; everything else stays metadata.
            return thread with { Messages = [.. thread.Messages.Select(m => m == people[0] ? m with { HasCalendarPart = true } : m)] };
        }
        return thread;
    }

    private async Task TakeAsync(ThreadSummary thread, IReadOnlySet<string> known, DateTimeOffset now, Tally tally, CancellationToken ct)
    {
        if (TwoWayRule.Read(thread) is not { } read)
        {
            return;
        }
        if (!WarmContactRule.Counts(read.Address, Mail.Name(read.From), known))
        {
            tally.Personal++;
            return;
        }
        var fresh = ConversationMerge.NewEmail(read, thread);
        var stored = await rows.FindAsync(fresh.PersonKey, ct) ?? (fresh.MatchKey is { } match ? await rows.FindByMatchKeyAsync(match, ct) : null);
        var row = stored is null ? fresh : ConversationMerge.Merge(stored, fresh);
        if (StopRule.Read(thread, row.FirstTwoWayAt) is { } stop)
        {
            row = ConversationMerge.Stop(row, stop);
            tally.Stopped += row.State != stored?.State ? 1 : 0;
        }
        if (stored is null && DueRule.IsStale(row, now))
        {
            row = row with { State = ConversationState.Closed, CloseReason = "stale" };
        }
        if (stored is null || row != stored)
        {
            await rows.UpsertAsync(row, now, ct);
            tally.Upserted++;
        }
    }

    private async Task QuietAsync(IEnumerable<Conversation> drafted, DateTimeOffset now, Tally tally, CancellationToken ct)
    {
        foreach (var row in drafted)
        {
            bool exists = row.DraftId is { } id && await drafts.DraftExistsAsync(id, ct);
            var thread = await gmail.ReadSummaryAsync(row.ThreadIds[0], ct);
            switch (QuietRule.Next(row, exists, thread, now))
            {
                case DraftedOutcome.Answered:
                    await rows.UpsertAsync(row with { State = ConversationState.Open, FollowUpCount = row.FollowUpCount + 1 }, now, ct);
                    tally.Upserted++;
                    break;
                case DraftedOutcome.Quiet:
                    await rows.UpsertAsync(row with { State = ConversationState.Quiet, FollowUpCount = row.FollowUpCount + 1 }, now, ct);
                    tally.Quiet++;
                    break;
                case DraftedOutcome.Declined:
                    await rows.SetStateAsync(row.Id, ConversationState.Closed, "declined", ct);
                    tally.Stopped++;
                    break;
            }
        }
    }

    private async Task<int> IngestLinkedInAsync(DateTimeOffset now, Tally tally, CancellationToken ct)
    {
        // A folder is listed, never asked whether it exists: the share answers ExistsAsync for files only, and a
        // missing folder lists as empty.
        int read = 0;
        foreach (var entry in (await share.ListFilesAsync(NetworkFiles.LinkedInInbox, ct)).Where(e => e.Name.EndsWith(".tsv", StringComparison.Ordinal)).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            string path = $"{NetworkFiles.LinkedInInbox}/{entry.Name}";
            var batch = LinkedInSpool.Parse(TrackerCsvText.Decode(await share.ReadAsync(path, ct)), entry.Name);
            foreach (var fresh in batch.Rows)
            {
                var stored = await rows.FindAsync(fresh.PersonKey, ct) ?? (fresh.MatchKey is { } match ? await rows.FindByMatchKeyAsync(match, ct) : null);
                var row = stored is null ? fresh : ConversationMerge.Merge(stored, fresh);
                if (stored is null && DueRule.IsStale(row, now))
                {
                    row = row with { State = ConversationState.Closed, CloseReason = "stale" };
                }
                await rows.UpsertAsync(row, now, ct);
                tally.Upserted++;
                read++;
            }
            await share.EnsureFolderAsync($"{NetworkFiles.LinkedInInbox}/done", ct);
            await share.MoveAsync(path, $"{NetworkFiles.LinkedInInbox}/done/{entry.Name}", ct);
            if (batch.Refused.Count > 0)
            {
                await share.AppendAsync($"{NetworkFiles.LinkedInInbox}/refused.log", TrackerCsvText.Encode(string.Concat(batch.Refused.Select(r => $"{entry.Name} {r}\n"))), ct);
            }
        }
        return read;
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await share.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await share.ReadAsync(path, ct)) : string.Empty;

    /// <summary>The feed a pass gets when it is handed none: no posts, so no draft names one.</summary>
    private sealed class NoPosts : IBlogPosts
    {
        /// <summary>The one instance.</summary>
        public static readonly NoPosts Instance = new();

        /// <inheritdoc />
        public Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BlogPost>>([]);
    }

    [GeneratedRegex(@"^(Invitation|Updated invitation|Accepted|Declined|Tentatively accepted):|invited you to", RegexOptions.IgnoreCase)]
    private static partial Regex Invitation();

    /// <summary>The pass's running counts.</summary>
    private sealed class Tally
    {
        /// <summary>Rows written or moved.</summary>
        public int Upserted { get; set; }

        /// <summary>Rows stopped or closed.</summary>
        public int Stopped { get; set; }

        /// <summary>Rows made Quiet.</summary>
        public int Quiet { get; set; }

        /// <summary>Strangers on a personal address, left out.</summary>
        public int Personal { get; set; }

        /// <summary>Rows owed a follow-up this Monday.</summary>
        public int Due { get; set; }

        /// <summary>Drafts, or would-drafts, that passed their checks.</summary>
        public int Drafted { get; set; }

        /// <summary>Due rows whose render failed a check.</summary>
        public int RenderFailed { get; set; }

        /// <summary>Drafts, or would-drafts, that name a post.</summary>
        public int Posts { get; set; }

        /// <summary>LinkedIn spool rows read.</summary>
        public int LinkedIn { get; set; }

        /// <summary>Threads left unread for want of time.</summary>
        public int Cut { get; set; }
    }
}
