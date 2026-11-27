namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The postings the morning's apply lane may work: the CONFIRMED LIVE and READY TO APPLY tables of the sweeps written in
/// the last 21 days, newest first, then the picker's passed rows of the last three days (FreshPostings), and the leads
/// scored 8 or above. A slot run reads none of them: its one posting is
/// the caught role's own.
/// </summary>
public static class ApplyPostings
{
    private const string ReopenList = "nightly/apply-reopen.tsv";
    private const string StandardAnswers = "standard-answers.md";

    /// <summary>Every posting the morning's lists put forward, before any gate.</summary>
    /// <param name="share">The share.</param>
    /// <param name="day">The Central day.</param>
    /// <param name="ct">Stops the reads.</param>
    public static async Task<List<Posting>> ReadAsync(IShare share, DateOnly day, CancellationToken ct)
    {
        var postings = new List<Posting>();
        foreach (string pattern in new[] { "JOBS-*.md", "priority-jobs-*.md", "APPLY-SWEEP-*.md" })
        {
            foreach (ShareFile file in (await share.ListAsync(string.Empty, pattern, ct)).Where(file => day.DayNumber - DateOnly.FromDateTime(file.Changed.UtcDateTime).DayNumber <= ApplyBoards.NewestDays).OrderByDescending(file => file.Path, StringComparer.Ordinal))
            {
                postings.AddRange(ApplyBoards.Confirmed(file.Path, await share.ReadAsync(file.Path, ct)));
            }
        }
        foreach (string fresh in new[] { FreshPostings.PathFor(day), FreshPostings.PathFor(day.AddDays(-1)) })
        {
            if (await share.ExistsAsync(fresh, ct))
            {
                postings.AddRange(FreshPostings.Read(fresh, await share.ReadAsync(fresh, ct), day));
                break;
            }
        }
        postings.AddRange(ApplyBoards.Scored(await share.ReadAsync("leads/scored.tsv", ct), await share.ReadAsync("nightly/LEADS.md", ct), day));
        return postings;
    }

    /// <summary>
    /// The postings less the ones the lane read and skipped on their forms in the last week (<see cref="ApplySkips"/>),
    /// and less every other posting on the board of a company whose form stopped the lane for the company's own reason
    /// (<see cref="ApplySkips.Boards"/>), as one count line each. A form the reopen list names is worked first, never
    /// held back by the memory, and a slot run's caught role is its own posting.
    /// </summary>
    /// <param name="share">The share.</param>
    /// <param name="reads">The morning's reads.</param>
    /// <param name="postings">The postings the tracker has not closed.</param>
    /// <param name="dropped">Where the count line goes.</param>
    /// <param name="ct">Stops the reads.</param>
    public static async Task<List<Posting>> Unskipped(IShare share, DayReads reads, List<Posting> postings, List<Dropped> dropped, CancellationToken ct)
    {
        if (reads.Role is not null || !await share.ExistsAsync(WritePolicy.SkipsTable, ct))
        {
            return postings;
        }
        string table = await share.ReadAsync(WritePolicy.SkipsTable, ct);
        var skipped = ApplySkips.Remembered(table, reads.Day);
        var boards = ApplySkips.Boards(table, reads.Day);
        var reopened = skipped.Count > 0 && await share.ExistsAsync(ReopenList, ct) ? ReopenKeys(await share.ReadAsync(ReopenList, ct)).ToHashSet(StringComparer.Ordinal) : [];
        if (skipped.Count > 0 && (await share.ListAsync(string.Empty, StandardAnswers, ct)).FirstOrDefault() is { } answers)
        {
            reopened.UnionWith(ApplySkips.Answered(table, reads.Day, RunSlot.Day(answers.Changed)));
        }
        skipped.ExceptWith(reopened);
        int held = postings.RemoveAll(posting => skipped.Contains(ApplySkips.Key(posting.Link)));
        if (held > 0)
        {
            dropped.Add(new Dropped($"{held} postings", $"off the list before the gates: the lane read their forms and skipped them in the last {ApplySkips.Days} days ({WritePolicy.SkipsTable})"));
        }
        int company = postings.RemoveAll(posting => ApplySkips.Board(posting.Link) is { } board && boards.Contains(board) && !reopened.Contains(ApplySkips.Key(posting.Link)));
        if (company > 0)
        {
            dropped.Add(new Dropped($"{company} postings", $"off the list before the gates: another of the company's forms stopped the lane in the last {ApplySkips.Days} days on a signature page, an unbanked question, an account wall or its own site ({WritePolicy.SkipsTable})"));
        }
        return postings;
    }

    private static IEnumerable<string> ReopenKeys(string list) =>
        list.Split('\n').Select(line => line.Split('\t')[0]).Where(cell => cell.StartsWith("http", StringComparison.OrdinalIgnoreCase)).Select(ApplySkips.Key);

    /// <summary>
    /// The linked postings, once each, less every one the tracker shows submitted (RULE 76 f4): those leave before the
    /// gates as one count line, never a skip row each, so a run's skip list holds only what a gate decided today.
    /// </summary>
    /// <param name="postings">Every posting the lists put forward.</param>
    /// <param name="gate">The tracker's gates.</param>
    /// <param name="dropped">Where the count line goes.</param>
    public static List<Posting> Unapplied(IEnumerable<Posting> postings, PersonGate gate, List<Dropped> dropped)
    {
        var linked = postings.Where(posting => posting.Link.Trim().Length > 0).DistinctBy(posting => posting.Link).ToList();
        int applied = linked.RemoveAll(posting => gate.Applied(posting.Company, posting.Link, posting.Role));
        if (applied > 0)
        {
            dropped.Add(new Dropped($"{applied} postings", "off the list before the gates: the tracker shows them submitted"));
        }
        return linked;
    }
}
