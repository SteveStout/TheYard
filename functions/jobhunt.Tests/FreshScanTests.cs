using Microsoft.Extensions.Logging;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The fresh pass (ADR-118, the addendum on the fresh pass): a posting new to the table is added once with the instant
/// its board gives, its description and nothing else; a posting the table holds, open or closed, is not touched; a
/// silent board is counted and the next board is read; the budget leaves boards and the line says how many; a row the
/// other scan wrote first is known, not a failure; a light list is read whole only for an id the table does not hold;
/// and the timer does nothing in the scan's hour or with its switch off.
/// </summary>
public sealed class FreshScanTests
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 11, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset WentUp = new(2026, 11, 11, 2, 52, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_new_id_is_added_once_with_its_instant_and_a_second_pass_adds_nothing()
    {
        var book = new PostingsTests.Postings();
        var boards = new Boards { ["acme"] = [Job("1", "Staff .NET Engineer") with { PublishedAt = WentUp }] };
        var scan = new FreshScan(new Watchlist(new BoardCompany(1, "lever", "acme") { Key = "acme.com" }), boards, new Clock(Evening), book);

        var first = await scan.RunAsync(CancellationToken.None);
        var second = await scan.RunAsync(CancellationToken.None);

        var row = Assert.Single(book.Rows);
        Assert.Equal((WentUp, Evening, "acme.com"), (row.Posting.PublishedAt!.Value, row.FirstSeen, row.CompanyKey));
        Assert.Single(book.Described);
        Assert.Equal("1 boards, 1 answered, 1 postings, 1 new, 0 failed, 0 s", first.Line);
        Assert.Equal("1 boards, 1 answered, 1 postings, 0 new, 0 failed, 0 s", second.Line);
        Assert.Equal(0, book.Touches);
    }

    [Fact]
    public async Task A_known_open_id_is_not_touched_and_a_known_closed_id_is_not_added_again()
    {
        var book = new PostingsTests.Postings();
        var watchlist = new Watchlist(new BoardCompany(1, "ashby", "acme"));
        var boards = new Boards { ["acme"] = [Job("open", "Lead Engineer"), Job("closed", "Staff Engineer")] };
        await new BoardScan(watchlist, boards, new Clock(Evening.AddDays(-40)), book).RunAsync(CancellationToken.None);
        boards["acme"] = [Job("open", "Lead Engineer")];
        await new BoardScan(watchlist, boards, new Clock(Evening.AddDays(-39)), book).RunAsync(CancellationToken.None);
        (int touched, int marked) = (book.Touches, watchlist.Marked);
        var seen = book.Rows.Select(r => (r.LastSeen, r.Closed)).ToList();
        // Both back on the board, with a new title on the open one: the fresh pass leaves them for the 1 AM scan.
        boards["acme"] = [Job("open", "Principal Engineer"), Job("closed", "Staff Engineer")];

        var pass = await new FreshScan(watchlist, boards, new Clock(Evening), book).RunAsync(CancellationToken.None);

        Assert.Equal((2, 0, 0), (book.Rows.Count, pass.Fresh, pass.Failed));
        Assert.Equal((touched, marked), (book.Touches, watchlist.Marked));
        Assert.Equal(seen, book.Rows.Select(r => (r.LastSeen, r.Closed)));
        Assert.Equal("Lead Engineer", book.Rows.Single(r => r.Posting.PostingId == "open").Posting.Title);
        Assert.Equal(0, book.Duplicates);
    }

    [Fact]
    public async Task A_silent_board_is_counted_and_the_next_board_is_read()
    {
        var book = new PostingsTests.Postings();
        var watchlist = new Watchlist(new BoardCompany(1, "lever", "quiet"), new BoardCompany(2, "lever", "acme"));
        var boards = new Boards { ["acme"] = [Job("1", "Lead Engineer"), Job("2", "Staff Engineer")] };

        var pass = await new FreshScan(watchlist, boards, new Clock(Evening), book).RunAsync(CancellationToken.None);

        Assert.Equal("2 boards, 1 answered, 2 postings, 2 new, 0 failed, 0 s", pass.Line);
        Assert.Equal(["acme", "acme"], book.Rows.Select(r => r.Posting.BoardToken));
    }

    [Fact]
    public async Task The_budget_leaves_boards_and_the_line_says_how_many()
    {
        var book = new PostingsTests.Postings();
        var watchlist = new Watchlist(new BoardCompany(1, "lever", "a"), new BoardCompany(2, "lever", "b"), new BoardCompany(3, "lever", "c"));
        var clock = new Clock(Evening);
        var boards = new Boards { ["a"] = [Job("1", "Lead Engineer")], ["b"] = [Job("2", "Lead Engineer")], ["c"] = [Job("3", "Lead Engineer")] };
        boards.Asked = () => clock.Pass(TimeSpan.FromMinutes(3));

        var pass = await new FreshScan(watchlist, boards, clock, book).RunAsync(CancellationToken.None);

        Assert.Equal("3 boards, 2 answered, 2 postings, 2 new, 0 failed, 360 s, 1 left at the 5-minute budget", pass.Line);
        Assert.Equal((1, 360), (pass.Left, pass.Seconds));
    }

    [Fact]
    public async Task A_row_the_other_scan_wrote_first_is_known_and_never_a_failure()
    {
        var book = new PostingsTests.Postings();
        var raced = Job("1", "Lead Engineer");
        book.WrittenAfterKnown.Add(BoardScan.Of("greenhouse", "acme", raced));
        var boards = new Boards { ["acme"] = [raced, Job("2", "Staff Engineer")] };

        var pass = await new FreshScan(new Watchlist(new BoardCompany(1, "greenhouse", "acme")), boards, new Clock(Evening), book).RunAsync(CancellationToken.None);

        Assert.Equal((1, 0, 1), (pass.Fresh, pass.Failed, book.Duplicates));
        Assert.Equal(2, book.Rows.Count);
        Assert.Single(book.Described);
    }

    [Fact]
    public async Task A_light_list_is_read_whole_only_when_it_shows_an_id_the_table_does_not_hold()
    {
        var book = new PostingsTests.Postings();
        var watchlist = new Watchlist(new BoardCompany(1, "greenhouse", "acme"));
        var whole = Job("1", "Staff Engineer") with { PublishedAt = WentUp };
        var boards = new Boards { ["acme"] = [whole], Light = true };

        await new FreshScan(watchlist, boards, new Clock(Evening), book).RunAsync(CancellationToken.None);
        await new FreshScan(watchlist, boards, new Clock(Evening.AddMinutes(15)), book).RunAsync(CancellationToken.None);

        Assert.Equal(1, boards.WholeReads);
        var row = Assert.Single(book.Rows);
        Assert.Equal(("text of Staff Engineer", WentUp), (row.Posting.Text, row.Posting.PublishedAt!.Value));
    }

    [Fact]
    public async Task A_new_posting_at_a_board_with_one_closed_in_30_days_is_linked_as_its_re_post()
    {
        var book = new PostingsTests.Postings();
        var watchlist = new Watchlist(new BoardCompany(1, "lever", "acme"));
        var boards = new Boards { ["acme"] = [Job("a", "Staff Software Engineer, Payments")] };
        await new BoardScan(watchlist, boards, new Clock(Evening.AddDays(-9)), book).RunAsync(CancellationToken.None);
        boards["acme"] = [];
        await new BoardScan(watchlist, boards, new Clock(Evening.AddDays(-6)), book).RunAsync(CancellationToken.None);
        boards["acme"] = [Job("b", "Senior Software Engineer, Payments")];

        await new FreshScan(watchlist, boards, new Clock(Evening), book).RunAsync(CancellationToken.None);

        var link = Assert.Single(book.Changes, c => c.Field == "repost_of");
        Assert.Equal(("1", "2"), (link.Old, link.New));
    }

    [Fact]
    public async Task The_timer_does_nothing_in_the_scan_s_hour_or_with_its_switch_off()
    {
        string? before = Environment.GetEnvironmentVariable("FRESH");
        try
        {
            using var scratch = new Scratch();
            // 1:15 AM Central under standard time is 07:15 UTC.
            var scanHour = new Clock(new DateTimeOffset(2026, 11, 11, 7, 15, 0, TimeSpan.Zero));
            Environment.SetEnvironmentVariable("FRESH", null);
            var inTheHour = new Lines();
            await new FreshScanTimer(scratch.Store, new NoHttp(), scanHour, inTheHour).RunAsync(null!, CancellationToken.None);

            Environment.SetEnvironmentVariable("FRESH", "OFF");
            var off = new Lines();
            await new FreshScanTimer(scratch.Store, new NoHttp(), new Clock(Evening), off).RunAsync(null!, CancellationToken.None);

            Assert.StartsWith("process:FreshScan outcome=unchanged", Assert.Single(inTheHour.Written), StringComparison.Ordinal);
            Assert.Contains("detail=scan_hour", inTheHour.Written[0], StringComparison.Ordinal);
            Assert.Contains("detail=switch_off", Assert.Single(off.Written), StringComparison.Ordinal);
            Assert.False(await scratch.Store.ExistsAsync(FreshScan.Log, CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRESH", before);
        }
    }

    [Fact]
    public void The_run_line_carries_the_pass_s_seven_counts()
    {
        var pass = new FreshPass(145, 144, 12470, 3, 0, 0, 61, TimeSpan.FromMinutes(5));

        string text = FreshScanTimer.Counted(new RunLine("FreshScan", Evening, [0, 15, 30, 45]), pass).Text(Evening.AddSeconds(61));

        Assert.Equal("process:FreshScan outcome=success took_ms=61000 started=2026-11-11T03:00:00Z answered=144 boards=145 failed=0 fresh=3 left=0 read=12470 took_s=61", text);
        Assert.Equal("145 boards, 144 answered, 12470 postings, 3 new, 0 failed, 61 s", pass.Line);
    }

    private static BoardPosting Job(string id, string title) =>
        new(title, "text of " + title, new DateOnly(2026, 11, 10)) { Id = id, Url = "https://x/" + id, Location = "Remote" };

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => now.AddTicks(_ticks);

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Pass(TimeSpan span) => _ticks += span.Ticks;
    }

    /// <summary>Boards by token; with Light, each list comes back without its descriptions, the way Greenhouse's does.</summary>
    private sealed class Boards : Dictionary<string, IReadOnlyList<BoardPosting>>, IBoardReader
    {
        public bool Light { get; init; }

        public int WholeReads { get; private set; }

        public Action Asked { get; set; } = () => { };

        public Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct)
        {
            WholeReads++;
            Asked();
            return Task.FromResult(TryGetValue(token, out var postings) ? postings : null);
        }

        public async Task<BoardList?> ListAsync(string kind, string token, CancellationToken ct)
        {
            if (!Light)
            {
                return await PostingsAsync(kind, token, ct) is { } whole ? new BoardList(whole, Whole: true) : null;
            }
            Asked();
            return TryGetValue(token, out var postings) ? new BoardList([.. postings.Select(p => p with { Text = "" })], Whole: false) : null;
        }
    }

    private sealed class Watchlist(params BoardCompany[] boards) : ICompanyBook
    {
        public Task<CompanyRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult<CompanyRecord?>(null);

        public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) => Task.FromResult(0);

        public Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BoardCompany>>(boards);

        public int Marked { get; private set; }

        public Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct)
        {
            Marked++;
            return Task.CompletedTask;
        }
    }

    private sealed class Lines : ILogger<FreshScanTimer>
    {
        public List<string> Written { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Written.Add(formatter(state, exception));
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test");
    }
}
