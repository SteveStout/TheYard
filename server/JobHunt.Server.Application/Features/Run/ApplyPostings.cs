namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The postings the morning's apply lane may work: the CONFIRMED LIVE and READY TO APPLY tables of the sweeps written in
/// the last 21 days, newest first, then the picker's passed rows of the last three days (FreshPostings), and the leads
/// scored 8 or above. A slot run reads none of them: its one posting is
/// the caught role's own.
/// </summary>
public static class ApplyPostings
{
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
