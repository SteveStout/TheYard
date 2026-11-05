using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The postings' edges: the first harvest seeds the table once, with the file's own time as LastSeenAt so the night's
/// picker never reads a seeded row as seen tonight; a posting closed and seen again is open again with no change row;
/// a description that changed alone writes a description and no change row; the scan stops starting boards at its
/// budget and names how many were left; and a title's level words come out of the base a re-post is matched on.
/// </summary>
public sealed class PostingsSeedTests
{
    private static readonly DateTimeOffset Night = new(2026, 11, 6, 7, 0, 0, TimeSpan.Zero);

    private const string Harvest = """
        {"source": "lever", "slug": "acme", "company": "acme", "title": "Lead .NET Engineer", "url": "https://jobs.lever.co/acme/aa11", "location": "Remote", "remote": true, "date": "2026-10-01T00:00:00Z", "how": "title", "line": ".NET", "comp": "$170K – $205K"}
        {"source": "greenhouse", "slug": "globex", "company": "globex", "title": "Staff Engineer", "url": "https://job-boards.greenhouse.io/globex/jobs/42", "location": "Austin, TX", "remote": false, "date": "2026-09-01T00:00:00Z", "how": "body", "line": "C#", "comp": null}
        """;

    [Fact]
    public async Task The_first_harvest_seeds_once_with_the_file_s_own_time()
    {
        using var scratch = new Scratch();
        var postings = new PostingsTests.Postings();
        Assert.Equal("seed: no file", await new PostingsSeed(scratch.Store, postings).RunAsync(CancellationToken.None));

        scratch.Write(PostingSeed.File, Harvest);
        var written = File.GetLastWriteTimeUtc(Path.Combine(scratch.Root, "staging", "dotnetbook", "handoff", "postings.jsonl"));
        string first = await new PostingsSeed(scratch.Store, postings).RunAsync(CancellationToken.None);
        string second = await new PostingsSeed(scratch.Store, postings).RunAsync(CancellationToken.None);

        Assert.Equal($"seed: 2 postings from {PostingSeed.File}, 0 lines unread", first);
        Assert.Equal("seed: already seeded", second);
        Assert.Equal(2, postings.Rows.Count);
        Assert.All(postings.Rows, row => Assert.Equal(written, row.LastSeen.UtcDateTime));
        Assert.Equal(2, postings.Described.Count);
    }

    [Fact]
    public async Task A_schema_not_yet_published_seeds_nothing_and_says_so()
    {
        using var scratch = new Scratch();
        scratch.Write(PostingSeed.File, Harvest);
        var postings = new PostingsTests.Postings { Ready = false };
        Assert.Equal("seed blocked: no_postings", await new PostingsSeed(scratch.Store, postings).RunAsync(CancellationToken.None));
        Assert.Empty(postings.Rows);
    }

    [Fact]
    public async Task A_posting_closed_and_seen_again_is_open_again_with_no_change_row()
    {
        var book = new PostingsTests.Postings();
        var companies = new Board(new BoardCompany(1, "greenhouse", "acme"));
        var boards = new Reader { ["acme"] = [Job("1", "Principal .NET Engineer", "first text")] };
        await new BoardScan(companies, boards, new Clock(Night), book).RunAsync(CancellationToken.None);
        boards["acme"] = [];
        await new BoardScan(companies, boards, new Clock(Night.AddDays(1)), book).RunAsync(CancellationToken.None);
        Assert.NotNull(book.Rows[0].Closed);
        boards["acme"] = [Job("1", "Principal .NET Engineer", "first text")];

        string line = await new BoardScan(companies, boards, new Clock(Night.AddDays(2)), book).RunAsync(CancellationToken.None);

        Assert.EndsWith("postings_seen 1, postings_new 0, postings_changed 0, postings_closed 0, postings_failed 0", line, StringComparison.Ordinal);
        Assert.Null(book.Rows[0].Closed);
        Assert.Empty(book.Changes);
    }

    [Fact]
    public async Task A_description_that_changed_alone_is_written_and_is_not_a_change_row()
    {
        var book = new PostingsTests.Postings();
        var companies = new Board(new BoardCompany(1, "ashby", "acme"));
        var boards = new Reader { ["acme"] = [Job("1", "Staff Engineer", "first text")] };
        await new BoardScan(companies, boards, new Clock(Night), book).RunAsync(CancellationToken.None);
        boards["acme"] = [Job("1", "Staff Engineer", "second text")];

        await new BoardScan(companies, boards, new Clock(Night.AddDays(1)), book).RunAsync(CancellationToken.None);

        Assert.Equal(2, book.Described.Count);
        Assert.NotEqual(book.Described[0].Hash, book.Described[1].Hash);
        Assert.Empty(book.Changes);
    }

    [Fact]
    public async Task The_scan_stops_starting_boards_at_its_budget_and_names_the_boards_left()
    {
        var companies = new Board(new BoardCompany(1, "lever", "a"), new BoardCompany(2, "lever", "b"), new BoardCompany(3, "lever", "c"));
        var clock = new Clock(Night);
        var boards = new Reader { ["a"] = [Job("1", "Lead Engineer", "x")], ["b"] = [Job("2", "Lead Engineer", "y")], ["c"] = [Job("3", "Lead Engineer", "z")] };
        boards.Asked = () => clock.Pass(TimeSpan.FromMinutes(5));

        string line = await new BoardScan(companies, boards, clock).RunAsync(CancellationToken.None);

        Assert.Equal("3 boards, 2 answered, 2 postings, 0 .NET, 1 left at the 8-minute budget", line);
    }

    [Theory]
    [InlineData("Staff Software Engineer, Payments", "software engineer payments")]
    [InlineData("Sr. Software Engineer - Payments", "software engineer payments")]
    [InlineData("Principal Engineer (.NET)", "engineer .net")]
    public void A_title_s_base_drops_its_level_words(string title, string expected) => Assert.Equal(expected, PostingLevel.Base(title));

    private static BoardPosting Job(string id, string title, string text) =>
        new(title, text, new DateOnly(2026, 11, 1)) { Id = id, Url = "https://x/" + id, Location = "Remote" };

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => now.AddTicks(_ticks);

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Pass(TimeSpan span) => _ticks += span.Ticks;
    }

    private sealed class Reader : Dictionary<string, IReadOnlyList<BoardPosting>>, IBoardReader
    {
        public Action Asked { get; set; } = () => { };

        public Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct)
        {
            Asked();
            return Task.FromResult(TryGetValue(token, out var postings) ? postings : null);
        }
    }

    private sealed class Board(params BoardCompany[] boards) : ICompanyBook
    {
        public Task<CompanyRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult<CompanyRecord?>(null);

        public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) => Task.FromResult(0);

        public Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BoardCompany>>(boards);

        public Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;
    }
}
