namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The postings the morning's apply lane may work: the CONFIRMED LIVE and READY TO APPLY tables of the sweeps written in
/// the last 21 days, newest first, and the leads scored 8 or above. A slot run reads none of them: its one posting is
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
        postings.AddRange(ApplyBoards.Scored(await share.ReadAsync("leads/scored.tsv", ct), await share.ReadAsync("nightly/LEADS.md", ct), day));
        return postings;
    }
}
