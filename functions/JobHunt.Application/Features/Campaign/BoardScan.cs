namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The daily board scan (ADR-118, Decision 5, and its addendum on the 1 AM scan): once a day, in the 1 o'clock hour
/// Central, one request per company with a public board, keyless JSON. Each company is marked with the newest
/// posting whose title names .NET, which Track B ranks by. With the postings tables present every posting becomes a
/// row keyed by its board and its id: seen again moves LastSeenAt, a changed Title, Level, CompMin, CompMax or
/// RemoteStance writes one change row each, a posting gone from its board is closed, a new one at the same board
/// whose title matches one closed in 30 days within one level is linked as its re-post, and a description is
/// written on first sight or a new hash only. Without the tables the scan marks the companies as before and its
/// line says the postings are blocked. About 2 % of the Function's free grant a month at two hundred companies.
/// The boards are read least recently scanned first and the scan stops starting boards at <see cref="Budget"/>, so
/// a long night ends inside the host's ten minutes and the next night begins where it stopped.
/// </summary>
public sealed class BoardScan(ICompanyBook companies, IBoardReader boards, TimeProvider time, IPostingBook? postings = null)
{
    /// <summary>How long the scan keeps starting boards; under the host's ten-minute limit with room for the rest of the hour's work.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromMinutes(8);

    /// <summary>Scans every board once and answers the part's line: counts only.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        long started = time.GetTimestamp();
        var all = await companies.BoardsAsync(ct);
        bool keep = postings is not null && await postings.ReadyAsync(ct);
        int answered = 0, dotNet = 0, read = 0;
        var tally = new Tally();
        int left = 0;
        foreach (var company in all)
        {
            if (time.GetElapsedTime(started) >= Budget)
            {
                left++;
                continue;
            }
            var found = await boards.PostingsAsync(company.Kind, company.Token, ct);
            if (found is null)
            {
                tally.Failed++;
                continue;
            }
            answered++;
            read += found.Count;
            var newest = found.Where(p => IsDotNet(p.Title)).OrderByDescending(p => p.Posted ?? DateOnly.MinValue).FirstOrDefault();
            dotNet += found.Count(p => IsDotNet(p.Title));
            await companies.MarkScannedAsync(company.Id, newest?.Posted ?? (newest is null ? null : DateOnly.FromDateTime(CentralClock.Local(time.GetUtcNow()))), newest?.Title, time.GetUtcNow(), ct);
            if (keep)
            {
                await KeepAsync(company, found, tally, ct);
            }
        }
        string line = $"{all.Count} boards, {answered} answered, {read} postings, {dotNet} .NET" + (left > 0 ? $", {left} left at the {Budget.TotalMinutes:0}-minute budget" : "");
        return line + (postings is null ? "" : keep
            ? $", postings_seen {tally.Seen}, postings_new {tally.New}, postings_changed {tally.Changed}, postings_closed {tally.Closed}, postings_failed {tally.Failed}"
            : ", postings blocked: no_postings");
    }

    private async Task KeepAsync(BoardCompany company, IReadOnlyList<BoardPosting> found, Tally tally, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var stored = await postings!.BoardAsync(company.Kind, company.Token, now - PostingRules.RepostWindow, ct);
        var byId = stored.GroupBy(p => p.PostingId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var unchanged = new List<long>();
        foreach (var board in found.Where(p => p.Id.Length > 0).DistinctBy(p => p.Id))
        {
            var posting = Of(company.Kind, company.Token, board);
            seenIds.Add(posting.PostingId);
            tally.Seen++;
            if (byId.TryGetValue(posting.PostingId, out var known))
            {
                var changes = PostingRules.Changes(known, posting);
                bool described = known.ContentHash != posting.ContentHash;
                if (changes.Count == 0 && !described && known.ClosedAt is null)
                {
                    unchanged.Add(known.Key);
                    continue;
                }
                await postings.UpdateAsync(known.Key, posting, now, ct);
                foreach (var (field, old, value) in changes)
                {
                    await postings.ChangeAsync(known.Key, field, old, value, now, ct);
                }
                if (described)
                {
                    await postings.DescribeAsync(known.Key, posting.ContentHash, posting.Text, now, ct);
                }
                tally.Changed += changes.Count > 0 ? 1 : 0;
                continue;
            }
            tally.New += await AddNewAsync(postings, company, posting, stored, now, ct) ? 1 : 0;
        }
        if (unchanged.Count > 0)
        {
            await postings.SeenAsync(unchanged, now, ct);
        }
        var gone = stored.Where(p => p.ClosedAt is null && !seenIds.Contains(p.PostingId)).Select(p => p.Key).ToList();
        if (gone.Count > 0)
        {
            await postings.CloseAsync(gone, now, ct);
            tally.Closed += gone.Count;
        }
    }

    /// <summary>
    /// A posting the table does not hold, added the one way both scans add one: the row, its description, and its
    /// repost_of note when it replaces one of <paramref name="stored"/> closed in the last 30 days. False, with nothing
    /// written, when another scan wrote the row first.
    /// </summary>
    internal static async Task<bool> AddNewAsync(IPostingBook postings, BoardCompany company, Posting posting, IReadOnlyList<StoredPosting> stored, DateTimeOffset now, CancellationToken ct)
    {
        if (await postings.AddAsync(posting, company.Key, now, ct) is not { } key)
        {
            return false;
        }
        await postings.DescribeAsync(key, posting.ContentHash, posting.Text, now, ct);
        if (PostingRules.RepostOf(posting, stored, now) is { } original)
        {
            await postings.ChangeAsync(key, "repost_of", original.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), key.ToString(System.Globalization.CultureInfo.InvariantCulture), now, ct);
        }
        return true;
    }

    /// <summary>A board's answer as a posting row of this board.</summary>
    public static Posting Of(string kind, string token, BoardPosting board) =>
        new(kind, token, board.Id, board.Title, board.Location, board.Url, board.Posted)
        {
            RemoteStance = PostingRules.Stance(board.Workplace, board.Location, board.IsRemote),
            CompMin = board.CompMin,
            CompMax = board.CompMax,
            CompCurrency = board.CompCurrency,
            CompInterval = board.CompInterval,
            PublishedAt = board.PublishedAt,
            Text = board.Text,
        };

    /// <summary>Whether a posting's title names .NET or C#: the scan's count and the recency date read titles only.</summary>
    public static bool IsDotNet(string title) =>
        title.Contains(".NET", StringComparison.OrdinalIgnoreCase) || title.Contains("C#", StringComparison.Ordinal) || title.Contains("dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>The scan's counts for its line.</summary>
    private sealed class Tally
    {
        /// <summary>Postings read from boards that answered.</summary>
        public int Seen { get; set; }

        /// <summary>Postings written for the first time.</summary>
        public int New { get; set; }

        /// <summary>Postings with at least one compared field moved.</summary>
        public int Changed { get; set; }

        /// <summary>Postings closed because their board no longer lists them.</summary>
        public int Closed { get; set; }

        /// <summary>Boards that did not answer.</summary>
        public int Failed { get; set; }
    }
}
