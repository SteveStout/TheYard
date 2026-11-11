namespace JobHunt.Application.Features.Jobs;

/// <summary>
/// One tick of the pounce (pounce1110 step 2b), five minutes behind each fresh pass: every posting first seen in the
/// last two hours that has no PounceRoles row is judged by JobsGates with its text and then by the pounce gate, and
/// gets its row, passed or not, with the instant it went live; every passed role still untouched inside the 14-day
/// watch is looked for a first touch in the tracker rows and campaign emails since the oldest of them went live; and on
/// the tick that asks, one line for each of the last seven Central days. It sends nothing and touches no one. The
/// whole tracker is read only on a tick with a fresh posting (RULE 50 and applied-already need it), the tracker rows
/// since the oldest open role only on a tick with one, and a quiet tick reads neither.
/// </summary>
public sealed class PounceTick(IMergeStore store, IJobsPostings postings, ITrackerRows tracker, IPounceBook book, TimeProvider time)
{
    /// <summary>How far back a posting first seen counts as fresh.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromHours(2);

    /// <summary>How many Central days the day lines cover, today first.</summary>
    public const int Days = 7;

    /// <summary>Runs one tick; <paramref name="days"/> asks for the day lines whatever the tick found.</summary>
    public async Task<PounceTickResult> RunAsync(bool days, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(CentralClock.Local(now));
        var fresh = await postings.FreshAsync(now - Fresh, ct);
        var judged = fresh.Count == 0 ? [] : await JudgeAsync(fresh, now, today, ct);
        if (judged.Count > 0)
        {
            await book.AddAsync([.. judged.Select(j => j.Role)], ct);
        }

        var open = await book.OpenAsync(now - TimeSpan.FromDays(PounceClock.WatchDays), ct);
        int touched = 0;
        if (open.Count > 0)
        {
            var since = open.Min(o => o.LiveAt);
            var rows = await tracker.SinceAsync(since, ct);
            var emails = await book.EmailsSinceAsync(since, ct);
            foreach (var role in open)
            {
                if (PounceClock.FirstTouch(role.Company, role.LiveAt, rows, emails) is { } touch)
                {
                    await book.MarkTouchedAsync(role.Key, touch.At, touch.Channel, ct);
                    touched++;
                }
            }
        }

        var passed = judged.Where(j => j.Role.Passed).Select(j => (j.Role, j.Title, j.Url)).ToList();
        IReadOnlyList<PounceDay> lines = [];
        if (days || passed.Count > 0 || touched > 0)
        {
            var cohort = await book.CohortAsync(now - TimeSpan.FromDays(Days + 1), ct);
            lines = [.. Enumerable.Range(0, Days).Select(back => PounceClock.DayLine(today.AddDays(-back), cohort, now))];
        }
        int late = passed.Count(p => p.Role.FirstSeenAt - p.Role.LiveAt > TimeSpan.FromMinutes(PounceClock.LateMinutes));
        return new PounceTickResult(judged.Count, passed, judged.Count - passed.Count, touched, open.Count - touched, late, lines);
    }

    private async Task<List<(PounceRole Role, string Title, string Url)>> JudgeAsync(IReadOnlyList<FreshPosting> fresh, DateTimeOffset now, DateOnly today, CancellationToken ct)
    {
        var master = JobsPick.Applications(await tracker.RowsAsync(ct));
        var list = ApplicationCap.DoNotApplyList(await TextAsync("systemRules.md", ct));
        var blacklist = PounceRules.Blacklisted(await TextAsync("BLACKLIST.md", ct));
        var texts = await postings.TextsAsync([.. fresh.Select(f => f.Key)], ct);
        var recent = (await book.PassedSinceAsync(now - TimeSpan.FromDays(PounceRules.DuplicateDays), ct))
            .Select(p => JobsGates.Same(new Candidate("", p.Company, p.Title, "", "", null, "", "", null, "", "")))
            .ToHashSet(StringComparer.Ordinal);
        var judged = new List<(PounceRole, string, string)>();
        foreach (var f in fresh)
        {
            var candidate = f.Candidate with { Text = texts.TryGetValue(f.Key, out var text) ? text : "" };
            var verdict = JobsGates.Judge(candidate, master, list, today);
            string? failed = PounceRules.Gate(verdict, blacklist, recent);
            if (failed is null)
            {
                recent.Add(JobsGates.Same(candidate));
            }
            var (live, known) = PounceClock.LiveAt(f.PublishedAt, f.FirstSeenAt);
            failed = failed is { Length: > 300 } ? failed[..300] : failed;
            judged.Add((new PounceRole(f.Key, candidate.Company, live, known, f.FirstSeenAt, now, failed, verdict.BandFlag, PounceClock.BudgetFlag(live, today), f.CompanyKey), candidate.Title, candidate.Url));
        }
        return judged;
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;
}
