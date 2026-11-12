using System.Text;

namespace JobHunt.Application.Features.Ats;

/// <summary>
/// Puts a draft into a Gmail thread. There is no send here on purpose: the port has one write, a draft, and
/// ReplyDraftTests holds it to that.
/// </summary>
public interface IReplyDraftWriter
{
    /// <summary>Writes the draft into the thread, with the reply headers that keep it there, and answers its id.</summary>
    Task<string> CreateDraftAsync(ThreadReply reply, string text, CancellationToken ct);
}

/// <summary>What one reply pass did.</summary>
/// <param name="Read">Messages read from the inbox window.</param>
/// <param name="Waiting">Of them, from a person on a job thread (the tracker knows the address or the work domain) with no draft and no answer from Steve.</param>
/// <param name="Queued">Items queued this pass.</param>
/// <param name="Already">Messages an earlier pass queued.</param>
/// <param name="Placed">Drafts the items wrote that this pass put into Gmail.</param>
/// <param name="Skipped">Messages this pass found on a thread the tracker does not know, which get no item and no draft.</param>
public sealed record ReplyDraftPass(int Read, int Waiting, int Queued, int Already, int Placed, int Skipped = 0);

/// <summary>
/// A reply reaches Steve with a draft (ReplyDraft): every firing reads the inbox of the last two hours for a message a
/// person wrote on a job thread, one whose writer's address or work domain is on a tracker row, that has no draft and
/// whose newest message is not his, and queues one dispatcher item for each: the item under a name the dispatcher does
/// not read, then the marker, then the rename, so a pass that stops part way is finished by the next and a message gets
/// one item. A message on any other thread gets no item and no draft, and one log line, once. Then each draft an item
/// has written is put into its thread's Gmail drafts, with the reply headers that keep it there, by a writer that cannot
/// send, once, and only when live, after the thread is read again: a thread Steve has answered or that holds another
/// draft since gets no draft, and the written one is marked stale. A dry pass leaves the draft on the share.
/// </summary>
/// <param name="store">The job-hunt share.</param>
/// <param name="inbox">The inbox, read only.</param>
/// <param name="drafts">The draft writer, or null on a dry pass.</param>
/// <param name="time">The clock.</param>
public sealed class ReplyDrafts(IMergeStore store, IInboxReader inbox, IReplyDraftWriter? drafts, TimeProvider time)
{
    /// <summary>The most messages one firing reads.</summary>
    public const int Most = 50;

    /// <summary>The mail this reads: the inbox, the window, nothing Steve sent.</summary>
    public static MailSearch Search { get; } = new([], [], InboxOnly: true, NewerThanDays: 1, NotFromMe: true);

    /// <summary>One pass: queue an item for each waiting message, then place the drafts the items wrote.</summary>
    public async Task<ReplyDraftPass> RunAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await store.EnsureFolderAsync(ReplyDraft.Folder, ct);
        var log = new StringBuilder();
        int read = 0, waiting = 0, queued = 0, already = 0, skipped = 0;
        KnownContacts? known = null;
        var threads = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in await inbox.FindAsync(Search, Most, ct))
        {
            var message = await inbox.ReadAsync(id, ct);
            read++;
            if (now - message.At > ReplyDraft.Window || !HumanMail.IsPerson(message.Address) || !threads.Add(message.ThreadId))
            {
                continue;
            }
            if (await store.ExistsAsync(ReplyDraft.SkippedPath(message.Id), ct))
            {
                continue;
            }
            if (await store.ExistsAsync(ReplyDraft.Marker(message.Id), ct))
            {
                if (await store.ExistsAsync(ReplyDraft.TempPath(message.Id), ct))
                {
                    await store.ReplaceAsync(ReplyDraft.TempPath(message.Id), ReplyDraft.ReadyPath(message.Id), ct);
                    queued++;
                    log.Append($"{CentralClock.Local(now):yyyy-MM-dd HH:mm} queued {ReplyDraft.ReadyPath(message.Id)}, finishing a pass that stopped before it\n");
                    continue;
                }
                already++;
                continue;
            }
            if (await inbox.ThreadHasDraftAsync(message.ThreadId, ct)
                || await inbox.ThreadNewestSenderAsync(message.ThreadId, ct) is { } newest && Mail.Address(newest) == HumanMail.Steve)
            {
                continue;
            }
            known ??= await KnownAsync(ct);
            if (!ReplyDraft.IsJobThread(message.Address, known))
            {
                await store.WriteAsync(ReplyDraft.SkippedPath(message.Id), TrackerCsvText.Encode($"{now:O}\n"), ct);
                log.Append($"{CentralClock.Local(now):yyyy-MM-dd HH:mm} skipped {message.Id}: not a job thread, no tracker row holds the writer's address or work domain\n");
                skipped++;
                continue;
            }
            waiting++;
            var reply = new WaitingReply(message.Id, message.ThreadId, message.Address, message.Name, message.Subject, message.At, message.Text, message.MessageId, message.References);
            // The item first, under a name the dispatcher does not read, then the marker, then the rename: a pass that stops
            // before the marker starts over, and one that stops after it finds the item waiting and renames it, once.
            await store.EnsureFolderAsync(ReplyDraft.Ready, ct);
            await store.WriteAsync(ReplyDraft.TempPath(reply.Id), TrackerCsvText.Encode(ReplyDraft.Item(reply, drafts is not null, now)), ct);
            await store.WriteAsync(ReplyDraft.Marker(reply.Id), TrackerCsvText.Encode(ReplyDraft.MarkerText(reply, now)), ct);
            await store.ReplaceAsync(ReplyDraft.TempPath(reply.Id), ReplyDraft.ReadyPath(reply.Id), ct);
            queued++;
            log.Append($"{CentralClock.Local(now):yyyy-MM-dd HH:mm} queued {ReplyDraft.ReadyPath(reply.Id)} ({(drafts is null ? "dry" : "live")})\n");
        }
        int placed = drafts is null ? 0 : await PlaceAsync(drafts, log, now, ct);
        if (log.Length > 0)
        {
            await store.AppendAsync(ReplyDraft.Log, TrackerCsvText.Encode(log.ToString()), ct);
        }
        return new ReplyDraftPass(read, waiting, queued, already, placed, skipped);
    }

    /// <summary>Why a written draft is no longer wanted, read from the thread right before it would be placed, or null.</summary>
    private async Task<string?> StaleAsync(string thread, CancellationToken ct) =>
        await inbox.ThreadHasDraftAsync(thread, ct) ? "another draft is in the thread"
        : await inbox.ThreadNewestSenderAsync(thread, ct) is { } newest && Mail.Address(newest) == HumanMail.Steve ? "Steve has answered the thread"
        : null;

    /// <summary>The addresses and work domains the tracker holds; an absent tracker knows no one, so nothing is a job thread.</summary>
    private async Task<KnownContacts> KnownAsync(CancellationToken ct) =>
        ReplyDraft.Known(await store.ExistsAsync(MergePaths.Master, ct) ? TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct))) : []);

    /// <summary>Each written draft with no placed file goes into its thread once; its placed file holds the draft's id.</summary>
    private async Task<int> PlaceAsync(IReplyDraftWriter writer, StringBuilder log, DateTimeOffset now, CancellationToken ct)
    {
        int placed = 0;
        foreach (MergeEntry file in (await store.ListFilesAsync(ReplyDraft.Folder, ct)).Where(f => f.Name.EndsWith(".draft.md", StringComparison.Ordinal)).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            string id = file.Name[..^".draft.md".Length];
            if (await store.ExistsAsync(ReplyDraft.PlacedPath(id), ct) || await store.ExistsAsync(ReplyDraft.StalePath(id), ct) || !await store.ExistsAsync(ReplyDraft.Marker(id), ct)
                || ReplyDraft.ReadMarker(TrackerCsvText.Decode(await store.ReadAsync(ReplyDraft.Marker(id), ct))) is not { } to
                || ReplyDraft.Body(TrackerCsvText.Decode(await store.ReadAsync(ReplyDraft.DraftPath(id), ct))) is not { } body)
            {
                continue;
            }
            if (await StaleAsync(to.Thread, ct) is { } why)
            {
                await store.WriteAsync(ReplyDraft.StalePath(id), TrackerCsvText.Encode($"{why} {now:O}\n"), ct);
                log.Append($"{CentralClock.Local(now):yyyy-MM-dd HH:mm} not placed the draft for {id}: {why}\n");
                continue;
            }
            string draftId = await writer.CreateDraftAsync(to, body, ct);
            await store.WriteAsync(ReplyDraft.PlacedPath(id), TrackerCsvText.Encode($"{draftId} {now:O}\n"), ct);
            log.Append($"{CentralClock.Local(now):yyyy-MM-dd HH:mm} placed the draft for {id} in its thread, not sent\n");
            placed++;
        }
        return placed;
    }
}
