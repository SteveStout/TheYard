namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The watchlist grows from the share (the addendum on the 1 AM scan): every file in campaign/boards-inbox/, oldest
/// first, its boards added to campaign.Companies by the Function, the one writer, deduped on the board's kind and
/// token, until <see cref="BoardsInbox.Cap"/> companies have a board; the file moved to merged/ with one log line.
/// </summary>
public sealed class BoardImport(IMergeStore store, IWatchlist watchlist, TimeProvider time)
{
    /// <summary>Imports every waiting file and answers the part's line: counts only.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var files = (await store.ListFilesAsync(BoardsInbox.Folder, ct))
            .Where(f => f.Name.EndsWith(".tsv", StringComparison.Ordinal)).OrderBy(f => f.LastWritten).ToList();
        int added = 0, known = 0, other = 0, unread = 0, capped = 0;
        int count = files.Count == 0 ? 0 : await watchlist.BoardCountAsync(ct);
        foreach (var entry in files)
        {
            string path = $"{BoardsInbox.Folder}/{entry.Name}";
            var (rows, others, unreadRows) = BoardsInbox.Parse(TrackerCsvText.Decode(await store.ReadAsync(path, ct)));
            other += others;
            unread += unreadRows;
            int fileAdded = 0;
            foreach (var row in rows)
            {
                if (count >= BoardsInbox.Cap)
                {
                    capped++;
                    continue;
                }
                if (await watchlist.AddBoardAsync(row, "boards-inbox", time.GetUtcNow(), ct))
                {
                    added++;
                    fileAdded++;
                    count++;
                }
                else
                {
                    known++;
                }
            }
            var local = CentralClock.Local(time.GetUtcNow());
            await store.EnsureFolderAsync($"{BoardsInbox.Folder}/merged", ct);
            await store.MoveAsync(path, $"{BoardsInbox.Folder}/merged/{local:yyyyMMdd-HHmmss}-{entry.Name}", ct);
            await store.AppendAsync(BoardsInbox.Log, TrackerCsvText.Encode($"{local:yyyy-MM-dd HH:mm:ss} {entry.Name}: {rows.Count} boards read, {fileAdded} added, {others} other kinds, {unreadRows} unread\r\n"), ct);
        }
        return $"{files.Count} board files, {added} boards added, {known} known, {other} other kinds, {unread} unread, {capped} over the cap of {BoardsInbox.Cap}";
    }
}

/// <summary>
/// The postings' first rows: once, when the tables are present and empty, the first harvest's file is written as
/// rows with the time the file was written as their LastSeenAt, so the night's picker never reads them as seen
/// tonight.
/// </summary>
public sealed class PostingsSeed(IShareReader share, IPostingBook postings)
{
    /// <summary>Seeds once and answers the part's line.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        if (!await postings.ReadyAsync(ct))
        {
            return "seed blocked: no_postings";
        }
        if (await postings.CountAsync(ct) > 0)
        {
            return "seed: already seeded";
        }
        if (await share.StatAsync(PostingSeed.File, ct) is not { } file)
        {
            return "seed: no file";
        }
        var (rows, unread) = PostingSeed.Parse(TrackerCsvText.Decode(await share.ReadAsync(PostingSeed.File, ct)));
        foreach (var posting in rows)
        {
            long key = await postings.AddAsync(posting, null, file.LastWritten, ct);
            await postings.DescribeAsync(key, posting.ContentHash, posting.Text, file.LastWritten, ct);
        }
        return $"seed: {rows.Count} postings from {PostingSeed.File}, {unread} lines unread";
    }
}
