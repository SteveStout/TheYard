namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The fresh pass (ADR-118, the addendum on the fresh pass): every quarter hour outside the 1 o'clock hour, each board
/// on the watchlist is read for postings the table does not hold, and each one is added the way the daily scan adds a
/// new posting (<see cref="BoardScan.AddNewAsync"/>: the row with the instant the board says it went up, its
/// description, its repost_of note). It writes nothing else. No company is marked, and no row is seen again, changed or
/// closed: those stay the 1 AM scan's, which touches every row once a day. A board that does not answer is counted and
/// the pass goes on; a row another scan wrote first counts as known, never as a failure. A board with a light list
/// (Greenhouse without its descriptions) is read whole only when the list shows an id the table does not hold.
/// </summary>
public sealed class FreshScan(ICompanyBook companies, IBoardReader boards, TimeProvider time, IPostingBook postings)
{
    /// <summary>The share's log of the passes, one line each.</summary>
    public const string Log = "campaign/fresh.log";

    /// <summary>How long the pass keeps starting boards: a third of the quarter hour, so a slow pass ends before the next.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the pass keeps writing the rows its reads found: eight minutes, two inside the host's ten-minute limit.</summary>
    public TimeSpan WriteBudget { get; init; } = TimeSpan.FromMinutes(8);

    /// <summary>
    /// How many boards are read at once. The boards are a hundred and more public hosts, read one after another in about
    /// forty seconds, nearly all of it waiting on the network; six at a time cuts the pass's billed seconds. Boards of one
    /// provider share its host, so one host may see up to six reads at once (ADR-118, the addendum on the fresh pass's run time).
    /// </summary>
    public int AtOnce { get; init; } = 6;

    /// <summary>
    /// Reads every board once for postings the table does not hold and adds them; answers the pass's counts. The boards
    /// are read <see cref="AtOnce"/> at a time; the rows are added one board after another once the reads are in, so the
    /// database sees the same one writer it always did.
    /// </summary>
    public async Task<FreshPass> RunAsync(CancellationToken ct)
    {
        long started = time.GetTimestamp();
        var all = await companies.BoardsAsync(ct);
        var known = await postings.KnownAsync(ct);
        int answered = 0, read = 0, fresh = 0, failed = 0, left = 0;
        var found = new System.Collections.Concurrent.ConcurrentBag<(int Order, BoardCompany Company, IReadOnlyList<BoardPosting> New)>();
        await Parallel.ForEachAsync(all.Select((company, order) => (company, order)), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, AtOnce), CancellationToken = ct }, async (entry, token) =>
        {
            var company = entry.company;
            if (time.GetElapsedTime(started) >= Budget)
            {
                Interlocked.Increment(ref left);
                return;
            }
            if (await boards.ListAsync(company.Kind, company.Token, token) is not { } list)
            {
                return;
            }
            Interlocked.Increment(ref answered);
            Interlocked.Add(ref read, list.Postings.Count);
            var unknown = list.Postings.Select(p => p.Id).Where(id => id.Length > 0 && !known.Contains(Posting.Known(company.Kind, company.Token, id))).ToHashSet(StringComparer.Ordinal);
            if (unknown.Count == 0)
            {
                return;
            }
            var whole = list.Whole ? list.Postings : await boards.PostingsAsync(company.Kind, company.Token, token);
            if (whole is null)
            {
                Interlocked.Add(ref failed, unknown.Count);
                return;
            }
            found.Add((entry.order, company, [.. whole.Where(p => unknown.Contains(p.Id)).DistinctBy(p => p.Id)]));
        });
        foreach (var (_, company, news) in found.OrderBy(entry => entry.Order))
        {
            // The writes stop at their own line, so a pass ends inside the host's ten minutes: a board whose rows are not
            // written by then is left, and its postings, still unknown to the table, are the next pass's.
            if (time.GetElapsedTime(started) >= WriteBudget)
            {
                left++;
                continue;
            }
            var (added, missed) = await AddAsync(company, news, ct);
            fresh += added;
            failed += missed;
        }
        return new FreshPass(all.Count, answered, read, fresh, failed, left, (int)Math.Round(time.GetElapsedTime(started).TotalSeconds), Budget);
    }

    private async Task<(int Added, int Failed)> AddAsync(BoardCompany company, IEnumerable<BoardPosting> found, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var stored = await postings.BoardAsync(company.Kind, company.Token, now - PostingRules.RepostWindow, ct);
        int added = 0, failed = 0;
        foreach (var board in found)
        {
            try
            {
                added += await BoardScan.AddNewAsync(postings, company, BoardScan.Of(company.Kind, company.Token, board), stored, now, ct) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // One row the database refused is counted, and the next posting is tried; the next pass tries it again.
                failed++;
            }
        }
        return (added, failed);
    }
}

/// <summary>A board's list as the fresh pass reads it: its postings, and whether they are whole or a light list without descriptions.</summary>
/// <param name="Postings">The postings the board listed.</param>
/// <param name="Whole">True when each posting carries its description and pay, as <see cref="IBoardReader.PostingsAsync"/> answers them.</param>
public sealed record BoardList(IReadOnlyList<BoardPosting> Postings, bool Whole);

/// <summary>One fresh pass's counts, and its line for campaign/fresh.log.</summary>
/// <param name="Boards">The boards on the watchlist.</param>
/// <param name="Answered">The boards that answered.</param>
/// <param name="Read">The postings the answering boards listed.</param>
/// <param name="Fresh">The postings added, new to the table.</param>
/// <param name="Failed">New postings not added: a board whose list answered and whose whole board did not, or a row the database refused.</param>
/// <param name="Left">The boards not started because the budget ran out.</param>
/// <param name="Seconds">How long the pass took.</param>
/// <param name="Budget">The pass's budget, which the line names when boards were left.</param>
public sealed record FreshPass(int Boards, int Answered, int Read, int Fresh, int Failed, int Left, int Seconds, TimeSpan Budget)
{
    /// <summary>"145 boards, 144 answered, 12470 postings, 3 new, 0 failed, 61 s", and the boards left at the budget when there were any.</summary>
    public string Line => $"{Boards} boards, {Answered} answered, {Read} postings, {Fresh} new, {Failed} failed, {Seconds} s"
        + (Left > 0 ? $", {Left} left at the {Budget.TotalMinutes:0}-minute budget" : "");
}
