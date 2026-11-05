using System.Globalization;

namespace JobHunt.Application.Features.Jobs;

/// <summary>
/// The night's picker (PROMPT-JOBHUNT-NIGHTLY-JOBS rung 3, D4), a command run in the 2 o'clock hour Central right before
/// the jobs lane is started: every posting the 1 AM scan saw open on an employer's board, and every alert lead that
/// passed the inbox pass's filter today or yesterday, held to JobsGates against the tracker's SQL copy and the do-not-
/// apply list; a description is read only for a posting the other gates passed. An alert lead whose company and title
/// the scan read tonight is dropped for the posting, which is confirmed. The result is the candidates file, with its
/// supply line first and every failure at its foot. With the postings tables not yet published it writes from the
/// alert leads alone and its line says so; a tracker copy that cannot be read falls back to the share's CSV, named on
/// the line. It writes nothing else.
/// </summary>
public sealed class JobsPick(IMergeStore store, IJobsPostings postings, ITrackerRows tracker, TimeProvider time)
{
    /// <summary>How far back a posting counts as seen tonight: the 1 AM scan, with room for a late one.</summary>
    public static readonly TimeSpan Tonight = TimeSpan.FromHours(30);

    /// <summary>Runs one pass and answers what it wrote.</summary>
    public async Task<JobsPickResult> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(CentralClock.Local(now));
        var (master, trackerWord) = await MasterAsync(ct);
        string rules = await store.ExistsAsync("systemRules.md", ct) ? TrackerCsvText.Decode(await store.ReadAsync("systemRules.md", ct)) : string.Empty;
        var list = ApplicationCap.DoNotApplyList(rules);

        bool ready = await postings.ReadyAsync(ct);
        var feed = ready ? await postings.OpenAsync(now - Tonight, ct) : [];
        var first = feed.Select(f => (f.Key, Verdict: JobsGates.Judge(f.Candidate, master, list, today))).ToList();
        var texts = await postings.TextsAsync([.. first.Where(f => f.Verdict.Failed is null).Select(f => f.Key)], ct);
        var feedVerdicts = first.Select(f => f.Verdict.Failed is null && texts.TryGetValue(f.Key, out var text)
            ? JobsGates.Judge(f.Verdict.Candidate with { Text = text }, master, list, today) : f.Verdict).ToList();

        var alerts = new List<Candidate>();
        foreach (var day in new[] { today, today.AddDays(-1) })
        {
            if (await store.ExistsAsync(JobsFiles.Leads(day), ct))
            {
                alerts.AddRange(JobsFiles.AlertRows(TrackerCsvText.Decode(await store.ReadAsync(JobsFiles.Leads(day), ct))));
            }
        }
        var seen = feed.Select(f => Same(f.Candidate)).ToHashSet(StringComparer.Ordinal);
        var alertVerdicts = alerts.DistinctBy(a => a.Url).Where(a => !seen.Contains(Same(a))).Select(a => JobsGates.Judge(a, master, list, today)).ToList();

        var all = feedVerdicts.Concat(alertVerdicts).ToList();
        var passed = all.Where(v => v.Failed is null).DistinctBy(v => Same(v.Candidate))
            .OrderBy(v => v.Candidate.Confirmed == "feed" ? 0 : 1).ThenBy(v => v.DaysOld).ToList();
        var failed = all.Where(v => v.Failed is not null).ToList();
        int boards = feed.Select(f => f.Candidate.Source + "|" + f.Candidate.Company).Distinct(StringComparer.Ordinal).Count();
        int confirmed = passed.Count(v => v.Candidate.Confirmed == "feed");
        string supply = $"scanned {feed.Count} postings from {boards} boards, {alerts.Count} alert leads; {passed.Count} passed the gates; {confirmed} confirmed by the feeds"
            + (ready ? "" : "; postings blocked: no_postings, alert leads alone") + $"; tracker {trackerWord}";

        string path = JobsFiles.Candidates(today);
        await store.EnsureFolderAsync("nightly", ct);
        await store.WriteAsync(path, TrackerCsvText.Encode(JobsFiles.Write(supply, passed, failed)), ct);
        return new JobsPickResult($"{path}: {supply}, {failed.Count} failed", path, feed.Count, boards, alerts.Count, passed.Count, confirmed);
    }

    private async Task<(IReadOnlyList<string[]> Rows, string Word)> MasterAsync(CancellationToken ct)
    {
        try
        {
            return (Applications(await tracker.RowsAsync(ct)), "sql");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var rows = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)));
            return (Applications(rows), "csv (the sql copy did not answer: " + ex.GetType().Name + ")");
        }
    }

    private static List<string[]> Applications(IEnumerable<string[]> rows) =>
        [.. rows.Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))];

    private static string Same(Candidate c) =>
        new string([.. c.Company.ToLowerInvariant().Where(char.IsLetterOrDigit)]) + "|" + PostingLevel.Base(c.Title) + "|" + c.Level;
}
