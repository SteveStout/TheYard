namespace JobHunt.Application.Features.Ats;

/// <summary>A message the reply pass settled, as its row in jobhunt.ReplyDrafts: no message text and no draft text.</summary>
/// <param name="MessageId">The Gmail message id.</param>
/// <param name="ThreadId">The Gmail thread id, when the pass knows it.</param>
/// <param name="Status">One of <see cref="ReplyDraftRow.Skipped"/>, <see cref="ReplyDraftRow.Queued"/>, <see cref="ReplyDraftRow.Placed"/> or <see cref="ReplyDraftRow.Stale"/>.</param>
/// <param name="Reason">Why it was skipped or went stale, or null.</param>
/// <param name="DraftId">Gmail's draft id once placed, or null.</param>
public sealed record ReplyDraftRow(string MessageId, string? ThreadId, string Status, string? Reason = null, string? DraftId = null)
{
    /// <summary>On a thread the tracker does not know: no item and no draft.</summary>
    public const string Skipped = "skipped";

    /// <summary>Its marker and dispatcher item are written.</summary>
    public const string Queued = "queued";

    /// <summary>Its draft is in the Gmail thread, not sent.</summary>
    public const string Placed = "placed";

    /// <summary>The thread changed before the draft was placed, so it never will be.</summary>
    public const string Stale = "stale";

    /// <summary>
    /// What the share's files say of a message, by their names in the folder: a skipped file, then placed, then stale,
    /// then the marker; null when the folder holds none of them.
    /// </summary>
    /// <param name="id">The Gmail message id.</param>
    /// <param name="names">The names the reply-drafts folder holds.</param>
    public static string? FromFiles(string id, IReadOnlySet<string> names) =>
        names.Contains($"{id}.skipped") ? Skipped
        : names.Contains($"{id}.placed") ? Placed
        : names.Contains($"{id}.stale") ? Stale
        : names.Contains($"{id}.md") ? Queued
        : null;
}

/// <summary>The reply pass's rows: whether the table is there, the rows of some ids, and one row written or moved.</summary>
public interface IReplyDraftRows
{
    /// <summary>True when jobhunt.ReplyDrafts exists.</summary>
    Task<bool> ReadyAsync(CancellationToken ct);

    /// <summary>The rows of these ids; an id with no row is left out.</summary>
    Task<IReadOnlyList<ReplyDraftRow>> FindAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    /// <summary>Writes the id's row, or moves it to this status, keeping a thread or draft id this row leaves null.</summary>
    Task UpsertAsync(ReplyDraftRow row, DateTimeOffset at, CancellationToken ct);
}

/// <summary>
/// The table is the switch: asked once a process, and while it is absent asked again once an hour, so the pass runs
/// on its files alone until the table is published and needs no setting to start writing rows.
/// </summary>
/// <param name="again">How long an absent answer is kept.</param>
public sealed class ReplyDraftSwitch(TimeSpan again)
{
    private bool present;
    private DateTimeOffset? askedAt;

    /// <summary>True once the table has been seen; an answer of absent is kept for <paramref name="again"/>.</summary>
    /// <param name="ask">Asks the store whether the table exists.</param>
    /// <param name="now">Now.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<bool> PresentAsync(Func<CancellationToken, Task<bool>> ask, DateTimeOffset now, CancellationToken ct)
    {
        if (present)
        {
            return true;
        }
        if (askedAt is { } at && now - at < again)
        {
            return false;
        }
        askedAt = now;
        present = await ask(ct);
        return present;
    }
}

/// <summary>
/// The reply pass's rows beside its files (datamove1123 version A1, the dual write): every decision the files make is
/// written as a row too, and at the end of a pass the rows of the window's ids are read back against the files. The
/// files still decide. An id the files settled that has no row gets one there, so the table fills by reading through
/// and no import runs. A SQL error never fails the pass: it is counted, and the rest of the pass writes no more rows.
/// </summary>
/// <param name="rows">The rows.</param>
public sealed class ReplyDraftLedger(IReplyDraftRows rows)
{
    private bool broken;

    /// <summary>The window's ids that have a row, after the read-through.</summary>
    public int Rows { get; private set; }

    /// <summary>Of those rows, the ones whose status is not what the files say.</summary>
    public int Mismatch { get; private set; }

    /// <summary>SQL calls that failed; after the first the pass asks SQL nothing more.</summary>
    public int SqlErrors { get; private set; }

    /// <summary>Writes one decision's row.</summary>
    public Task NoteAsync(ReplyDraftRow row, DateTimeOffset at, CancellationToken ct) =>
        GuardAsync(token => rows.UpsertAsync(row, at, token), ct);

    /// <summary>Reads the window's rows against the folder's names, writing the row a file-settled id is missing.</summary>
    /// <param name="window">The ids and threads the inbox search listed this pass.</param>
    /// <param name="names">The names the reply-drafts folder holds after the pass.</param>
    /// <param name="at">Now.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task CompareAsync(IReadOnlyList<(string Id, string? Thread)> window, IReadOnlySet<string> names, DateTimeOffset at, CancellationToken ct)
    {
        var ids = window.DistinctBy(w => w.Id).ToList();
        IReadOnlyList<ReplyDraftRow>? found = null;
        await GuardAsync(async token => found = await rows.FindAsync([.. ids.Select(w => w.Id)], token), ct);
        if (found is null)
        {
            return;
        }
        var byId = found.ToDictionary(r => r.MessageId, StringComparer.Ordinal);
        foreach (var (id, thread) in ids)
        {
            string? files = ReplyDraftRow.FromFiles(id, names);
            if (byId.TryGetValue(id, out var row))
            {
                Rows++;
                Mismatch += row.Status == files ? 0 : 1;
            }
            else if (files is not null)
            {
                await NoteAsync(new ReplyDraftRow(id, thread, files), at, ct);
                Rows += broken ? 0 : 1;
            }
        }
    }

    private async Task GuardAsync(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        if (broken)
        {
            return;
        }
        try
        {
            await call(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SqlErrors++;
            broken = true;
        }
    }
}
