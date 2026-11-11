using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// One pounce tick over hand-written fakes (pounce1110 step 2b): a fresh role that passes gets its row and its clock, one
/// that fails gets its row and no clock, a posting judged once is not judged again, a touch before the live instant, at
/// another company, to a recruiter or to a shared mailbox is no touch, the first of two touches wins, a quiet tick reads
/// no tracker, and the day lines. Then the statements' columns against the SQL project and the timer's log line.
/// </summary>
public sealed partial class PounceTickTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 16, 5, 0, TimeSpan.Zero);

    private static Candidate Role(string title = "Principal Software Engineer", string company = "Acme") =>
        new("feed:greenhouse", company, title, "https://job-boards.greenhouse.io/acme/jobs/1", "greenhouse", new DateOnly(2026, 11, 11), "Remote, US", "Remote", 210_000, "feed", "");

    private static string[] Row(string person, string company, string notes, string communication = "LinkedIn", string day = "11/11/2026") =>
        ["1", person, company, communication, day, "", notes];

    [Fact]
    public async Task A_fresh_role_that_passes_gets_a_row_and_a_clock_and_one_that_fails_gets_a_row_and_none()
    {
        using var scratch = new Scratch();
        var postings = new Postings();
        postings.Add(1, Role(), Now.AddMinutes(-20), Now.AddMinutes(-31), "C# on .NET and Azure");
        postings.Add(2, Role(title: "Principal Product Manager"), Now.AddMinutes(-20), null, "C# on .NET and Azure");
        var book = new Book(postings);
        var tracker = new Tracker { Since = { new TrackerTouch(Row("Pat Rowe", "Acme (VP Engineering", "CONNECTION REQUEST SENT BY CLAUDE"), Now.AddMinutes(-2)) } };

        var tick = await new PounceTick(scratch.Store, postings, tracker, book, new Fixed(Now)).RunAsync(false, CancellationToken.None);

        var passed = book.Rows[1];
        Assert.Equal((true, Now.AddMinutes(-31), true, Now.AddMinutes(-2), "invites"), (passed.Role.Passed, passed.Role.LiveAt, passed.Role.LiveKnown, passed.Touch, passed.Channel));
        Assert.Equal(("PASS", true, Now), (passed.Role.BandFlag, passed.Role.BudgetFlag, passed.Role.JudgedAt));
        var failed = book.Rows[2];
        Assert.Equal(("not a seat his targeting file names", Now.AddMinutes(-20), false, (DateTimeOffset?)null), (failed.Role.Failed, failed.Role.LiveAt, failed.Role.LiveKnown, failed.Touch));
        Assert.Equal((2, 1, 1, 1, 0, 0), (tick.Fresh, tick.Passed.Count, tick.Dropped, tick.Touched, tick.Waiting, tick.Late));
        Assert.Equal(PounceTick.Days, tick.Days.Count);
        Assert.Equal(new PounceDay(new DateOnly(2026, 11, 11), 1, 1, 29, 29, 0, 0), tick.Days[0]);
    }

    [Fact]
    public async Task A_posting_judged_once_is_not_judged_again_and_a_quiet_tick_reads_no_tracker()
    {
        using var scratch = new Scratch();
        var postings = new Postings();
        postings.Add(1, Role(title: "Principal Product Manager"), Now.AddMinutes(-20), null, "");
        var book = new Book(postings);
        var tracker = new Tracker();
        var pounce = new PounceTick(scratch.Store, postings, tracker, book, new Fixed(Now));

        await pounce.RunAsync(false, CancellationToken.None);
        var second = await pounce.RunAsync(false, CancellationToken.None);

        Assert.Single(book.Rows);
        Assert.Equal((0, 0, 0), (second.Fresh, second.Touched, second.Days.Count));
        Assert.Equal((1, 0), (tracker.WholeReads, tracker.SinceReads));
    }

    [Fact]
    public async Task A_touch_before_live_at_another_company_to_a_recruiter_or_a_shared_mailbox_is_no_touch_and_the_first_of_two_wins()
    {
        using var scratch = new Scratch();
        var postings = new Postings();
        var live = Now.AddHours(-3);
        postings.Add(1, Role(), Now.AddMinutes(-90), live, "C# on .NET");
        var book = new Book(postings);
        var tracker = new Tracker
        {
            Since =
            {
                new TrackerTouch(Row("Pat Rowe", "Acme (CTO)", "MESSAGE SENT"), live.AddMinutes(-1)),
                new TrackerTouch(Row("Pat Rowe", "Globex (CTO)", "MESSAGE SENT"), live.AddMinutes(5)),
                new TrackerTouch(Row("Ana Bell", "Acme", "CONNECTION REQUEST SENT. Senior Talent Acquisition Partner"), live.AddMinutes(6)),
                new TrackerTouch(Row("Pat Rowe", "Acme (CTO)", "STATUS: NO MESSAGE SENT"), live.AddMinutes(7)),
                new TrackerTouch(Row("Pat Rowe", "Acme (CTO)", "MESSAGE SENT", day: "11/10/2026"), live.AddMinutes(8)),
                new TrackerTouch(Row("Pat Rowe", "Acme (CTO)", "EMAIL SENT BY STEVE", "Email"), live.AddMinutes(95)),
                new TrackerTouch(Row("Lee Park", "Acme Inc (Director of Engineering", "INMAIL SENT"), live.AddMinutes(130)),
            },
        };
        book.Emails.Add(new EmailTouch("Acme", "info@acme.example", "Kim Hale", live.AddMinutes(10)));
        book.Emails.Add(new EmailTouch("Acme", "kim.hale@acme.example", "Kim Hale", live.AddMinutes(100)));

        await new PounceTick(scratch.Store, postings, tracker, book, new Fixed(Now)).RunAsync(false, CancellationToken.None);

        Assert.Equal((live.AddMinutes(95), "emails"), (book.Rows[1].Touch, book.Rows[1].Channel));
        var touch = PounceClock.FirstTouch("Acme", live, [], book.Emails);
        Assert.Equal((live.AddMinutes(100), "campaign"), touch!.Value);
        Assert.Null(PounceClock.FirstTouch("Acme", live, tracker.Since.Take(5), book.Emails.Take(1)));
    }

    [Fact]
    public async Task A_touch_found_is_never_moved_and_a_second_listing_of_one_role_is_a_duplicate()
    {
        using var scratch = new Scratch();
        var postings = new Postings();
        postings.Add(1, Role(), Now.AddMinutes(-30), null, "C# on .NET");
        postings.Add(2, Role() with { Url = "https://job-boards.greenhouse.io/acme/jobs/2", Location = "Remote, Canada" }, Now.AddMinutes(-29), null, "C# on .NET");
        var book = new Book(postings);
        var tracker = new Tracker { Since = { new TrackerTouch(Row("Pat Rowe", "Acme (CTO)", "MESSAGE SENT"), Now.AddMinutes(-10)) } };
        var pounce = new PounceTick(scratch.Store, postings, tracker, book, new Fixed(Now));

        var first = await pounce.RunAsync(false, CancellationToken.None);
        tracker.Since.Insert(0, new TrackerTouch(Row("Pat Rowe", "Acme (CTO)", "MESSAGE SENT"), Now.AddMinutes(-20)));
        await pounce.RunAsync(false, CancellationToken.None);

        Assert.Equal("the same role passed in the last 30 days", book.Rows[2].Role.Failed);
        Assert.Equal(Now.AddMinutes(-10), book.Rows[1].Touch);
        Assert.Equal((1, 1), (first.Passed.Count, first.Dropped));
    }

    [Fact]
    public void The_log_line_names_each_passed_role_with_its_live_and_seen_times_on_the_Central_clock()
    {
        var role = new PounceRole(1, "Acme", Now.AddMinutes(-31), true, Now.AddMinutes(-20), Now, null, "PASS", true);
        var tick = new PounceTickResult(3, [(role, "Staff Engineer | Payments\tTeam", "https://x/1")], 2, 0, 1, 0, []);
        Assert.Equal("2026-11-11 10:05:00 pounce: 3 fresh, 1 passed, 2 dropped, 0 touched, 1 waiting, 0 late; Acme | Staff Engineer Payments Team | live 2026-11-11 09:34 | seen 2026-11-11 09:45",
            PounceTimer.LogLine(CentralClock.Local(Now), "pounce", tick));
        var run = PounceTimer.Counted(new RunLine("Pounce", Now), tick);
        Assert.Equal(["dropped", "fresh", "late", "passed", "touched", "waiting"], run.Counts.Keys);
    }

    [Fact]
    public async Task A_share_that_does_not_answer_fails_no_tick_and_the_run_line_says_the_log_was_not_written()
    {
        using var scratch = new Scratch();
        var run = new RunLine("Pounce", Now);
        var log = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        await PounceTimer.AppendLogAsync(new Down(), log, run, "a line", CancellationToken.None);
        Assert.Equal(("success", 1L), (run.Outcome, run.Counts["log_failed"]));
        var written = new RunLine("Pounce", Now);
        await PounceTimer.AppendLogAsync(scratch.Store, log, written, "a line", CancellationToken.None);
        Assert.Equal("a line\n", scratch.Text(PounceTimer.Log));
        Assert.False(written.Counts.ContainsKey("log_failed"));
    }

    [Fact]
    public async Task A_tracker_row_with_no_numbered_Count_is_not_an_application_to_the_pounce_as_to_the_picker()
    {
        using var scratch = new Scratch();
        var postings = new Postings();
        postings.Add(1, Role(), Now.AddMinutes(-20), null, "C# on .NET");
        var book = new Book(postings);
        var tracker = new Tracker
        {
            Rows =
            {
                new[] { "NA", "NA", "Acme", "Greenhouse", "11/10/2026", "", "SUBMITTED: Principal Software Engineer" },
                new[] { "12", "NA", "Globex", "Greenhouse", "11/10/2026", "", "SUBMITTED: Principal Software Engineer" },
            },
        };
        postings.Add(2, Role(company: "Globex"), Now.AddMinutes(-20), null, "C# on .NET");

        await new PounceTick(scratch.Store, postings, tracker, book, new Fixed(Now)).RunAsync(false, CancellationToken.None);

        Assert.Null(book.Rows[1].Role.Failed);
        Assert.Equal("applied already, Count 12", book.Rows[2].Role.Failed);
    }

    [Fact]
    public async Task The_timer_with_no_database_is_blocked_and_says_so()
    {
        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CAMPAIGN_SQL")), "the test host has no database setting");
        using var scratch = new Scratch();
        var run = new RunLine("Pounce", Now);
        var lines = await new PounceTimer(scratch.Store, new Fixed(Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<PounceTimer>.Instance).TickAsync(run, true, "pounce", CancellationToken.None);
        Assert.Equal(["blocked: CAMPAIGN_SQL is not set"], lines);
        Assert.Equal(("blocked", "no_database"), (run.Outcome, run.Detail));
        Assert.False(scratch.Has(PounceTimer.Log));
    }

    [Fact]
    public void Every_column_the_pounce_reads_or_writes_is_declared_by_the_sql_project()
    {
        var tables = new[] { "Postings", "Companies", "Tracker", "PounceRoles", "EmailsSent", "Leads" }
            .ToDictionary(t => t, t => Column().Matches(File.ReadAllText(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables", t + ".sql"))).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal));
        var aliases = new Dictionary<string, string> { ["p"] = "Postings", ["c"] = "Companies", ["t"] = "Tracker", ["r"] = "PounceRoles", ["x"] = "PounceRoles", ["e"] = "EmailsSent", ["l"] = "Leads" };
        var statements = typeof(SqlPounceBook).Assembly.GetType("JobHunt.Infrastructure.Features.Jobs.PounceSql", throwOnError: true)!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var wrong = statements.SelectMany(sql => Qualified().Matches(sql)).Where(m => !tables[aliases[m.Groups[1].Value]].Contains(m.Groups[2].Value)).Select(m => m.Value).ToList();
        Assert.True(wrong.Count == 0, string.Join(", ", wrong));
        Assert.Equal(9, statements.Count);
        Assert.Equal(2, statements.Count(sql => WriteVerb().IsMatch(sql)));
        string table = File.ReadAllText(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables", "PounceRoles.sql"));
        Assert.DoesNotMatch(@"\[(Person|ContactName|Name)\]", table);
        Assert.Contains("WHERE [Passed] = 1 AND [FirstTouchAt] IS NULL", table, StringComparison.Ordinal);
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^\s*\[(\w+)\]", RegexOptions.Multiline)]
    private static partial Regex Column();

    [GeneratedRegex(@"\b([pctrxel])\.\[(\w+)\]")]
    private static partial Regex Qualified();

    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\b")]
    private static partial Regex WriteVerb();

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Postings : IJobsPostings
    {
        public List<FreshPosting> All { get; } = [];

        public Dictionary<long, string> Texts { get; } = [];

        public HashSet<long> Judged { get; } = [];

        public void Add(long key, Candidate role, DateTimeOffset seen, DateTimeOffset? published, string text)
        {
            All.Add(new FreshPosting(key, role, seen, published));
            Texts[key] = text;
        }

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<(long Key, Candidate Candidate)>> OpenAsync(DateTimeOffset seenSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<(long, Candidate)>>([]);

        public Task<IReadOnlyDictionary<long, string>> TextsAsync(IReadOnlyList<long> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<long, string>>(Texts.Where(t => keys.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value));

        public Task<IReadOnlyList<FreshPosting>> FreshAsync(DateTimeOffset seenSince, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FreshPosting>>([.. All.Where(p => p.FirstSeenAt >= seenSince && !Judged.Contains(p.Key))]);
    }

    private sealed class Tracker : ITrackerRows
    {
        public List<TrackerTouch> Since { get; } = [];

        public List<string[]> Rows { get; } = [];

        public int WholeReads { get; private set; }

        public int SinceReads { get; private set; }

        public Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct)
        {
            WholeReads++;
            return Task.FromResult<IReadOnlyList<string[]>>(Rows);
        }

        public Task<IReadOnlyList<TrackerTouch>> SinceAsync(DateTimeOffset since, CancellationToken ct)
        {
            SinceReads++;
            return Task.FromResult<IReadOnlyList<TrackerTouch>>([.. Since.Where(r => r.WrittenAt >= since)]);
        }
    }

    private sealed class Down : IMergeStore
    {
        public Task EnsureFolderAsync(string folder, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task WriteAsync(string path, byte[] bytes, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task ReplaceAsync(string tempPath, string path, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task MoveAsync(string path, string destination, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task AppendAsync(string path, byte[] bytes, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task<bool> ExistsAsync(string path, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task<byte[]> ReadAsync(string path, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task<MergeEntry?> StatAsync(string path, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct) => throw new IOException("the share did not answer");

        public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct) => throw new IOException("the share did not answer");
    }

    private sealed class Book(Postings postings) : IPounceBook
    {
        public Dictionary<long, (PounceRole Role, DateTimeOffset? Touch, string? Channel)> Rows { get; } = [];

        public List<EmailTouch> Emails { get; } = [];

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<OpenRole>> OpenAsync(DateTimeOffset liveSince, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OpenRole>>([.. Rows.Values.Where(r => r.Role.Passed && r.Touch is null && r.Role.LiveAt >= liveSince).Select(r => new OpenRole(r.Role.Key, r.Role.Company, r.Role.LiveAt))]);

        public Task<IReadOnlyList<(string Company, string Title)>> PassedSinceAsync(DateTimeOffset judgedSince, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(string, string)>>([.. Rows.Values.Where(r => r.Role.Passed && r.Role.JudgedAt >= judgedSince).Select(r => (r.Role.Company, postings.All.First(p => p.Key == r.Role.Key).Candidate.Title))]);

        public Task<IReadOnlyList<(DateTimeOffset Live, DateTimeOffset? Touch)>> CohortAsync(DateTimeOffset liveSince, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(DateTimeOffset, DateTimeOffset?)>>([.. Rows.Values.Where(r => r.Role.Passed && r.Role.LiveAt >= liveSince).Select(r => (r.Role.LiveAt, r.Touch))]);

        public Task<IReadOnlyList<EmailTouch>> EmailsSinceAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<EmailTouch>>([.. Emails.Where(e => e.SentAt >= since)]);

        public Task AddAsync(IReadOnlyList<PounceRole> roles, CancellationToken ct)
        {
            foreach (var role in roles)
            {
                Rows.TryAdd(role.Key, (role, null, null));
                postings.Judged.Add(role.Key);
            }
            return Task.CompletedTask;
        }

        public Task MarkTouchedAsync(long key, DateTimeOffset at, string channel, CancellationToken ct)
        {
            if (Rows[key].Touch is null)
            {
                Rows[key] = (Rows[key].Role, at, channel);
            }
            return Task.CompletedTask;
        }
    }
}
