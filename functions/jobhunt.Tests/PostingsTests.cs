using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The postings the board scan keeps (PROMPT-JOBHUNT-POSTINGS rungs 1 and 2): the level read from a title, the three
/// feeds' pay read from the shapes the boards answered on 5 November, two scans in a row making no second row, a
/// change row for each moved field, a posting gone from its board closed, a re-post one level down linked to the
/// one it replaced, a schema not yet published leaving the scan to mark companies as before, and the scan's own
/// 1 o'clock hour on both sides of the clock change.
/// </summary>
public sealed class PostingsTests
{
    private static readonly DateTimeOffset Night = new(2026, 11, 6, 7, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Principal Software Engineer", "Principal")]
    [InlineData("Senior Staff Software Engineer", "Staff")]
    [InlineData("Senior Engineering Manager, Platform", "Manager")]
    [InlineData("Lead Solutions Architect (.NET)", "Architect")]
    [InlineData("Director of Engineering", "Director")]
    [InlineData("Lead Software Engineer", "Lead")]
    [InlineData("Sr. Software Engineer", "Senior")]
    [InlineData("Software Engineer II", "Mid")]
    [InlineData("Product Designer", "Unknown")]
    public void A_title_reads_as_its_level(string title, string level) => Assert.Equal(level, PostingLevel.Of(title));

    [Fact]
    public void The_three_feeds_give_the_band_the_board_published_and_null_when_it_published_none()
    {
        var greenhouse = PublicBoardReader.Read("greenhouse", """{"jobs":[{"id":8638232002,"title":"Staff Backend Engineer","absolute_url":"https://job-boards.greenhouse.io/gitlab/jobs/8638232002","location":{"name":"Remote, United States"},"first_published":"2026-10-22T13:38:40-04:00","content":"&lt;p&gt;C# and Azure&lt;/p&gt;","pay_input_ranges":[{"min_cents":13920000,"max_cents":23520000,"currency_type":"USD","title":"United States Salary Range"}]},{"id":2,"title":"Designer","absolute_url":"https://x/2","location":{"name":"Berlin"},"content":""}]}""");
        var lever = PublicBoardReader.Read("lever", """[{"id":"6ed76ce8","text":"Lead Engineer","hostedUrl":"https://jobs.lever.co/acme/6ed76ce8","categories":{"location":"Remote - US"},"workplaceType":"remote","descriptionPlain":"Build .NET services","createdAt":1792800000000,"salaryRange":{"currency":"USD","interval":"per-year-salary","min":170000,"max":210000}}]""");
        var ashby = PublicBoardReader.Read("ashby", """{"jobs":[{"id":"34413f8d","title":"Principal Engineer","jobUrl":"https://jobs.ashbyhq.com/ramp/34413f8d","location":"New York, NY (HQ)","isRemote":true,"workplaceType":"Hybrid","publishedAt":"2026-10-07T17:12:35.753+00:00","descriptionPlain":"x","compensation":{"summaryComponents":[{"compensationType":"Salary","interval":"1 YEAR","currencyCode":"USD","minValue":211400,"maxValue":290600},{"compensationType":"EquityPercentage","interval":"NONE","currencyCode":null,"minValue":null,"maxValue":null}]}}]}""");

        Assert.Equal(("8638232002", 139200, 235200, "USD", "Remote, United States", "C# and Azure"), (greenhouse[0].Id, greenhouse[0].CompMin, greenhouse[0].CompMax, greenhouse[0].CompCurrency, greenhouse[0].Location, greenhouse[0].Text));
        Assert.Null(greenhouse[1].CompMax);
        Assert.Equal(("6ed76ce8", 170000, 210000, "per-year-salary", "remote"), (lever[0].Id, lever[0].CompMin, lever[0].CompMax, lever[0].CompInterval, lever[0].Workplace));
        Assert.Equal(("34413f8d", 211400, 290600, "Hybrid"), (ashby[0].Id, ashby[0].CompMin, ashby[0].CompMax, BoardScan.Of("ashby", "ramp", ashby[0]).RemoteStance));
        Assert.Equal("Remote", BoardScan.Of("greenhouse", "gitlab", greenhouse[0]).RemoteStance);
    }

    [Fact]
    public void Each_feed_keeps_the_instant_its_board_says_the_posting_went_up_and_null_when_it_gives_none()
    {
        var greenhouse = PublicBoardReader.Read("greenhouse", """{"jobs":[{"id":1,"title":"Staff Engineer","first_published":"2026-11-10T13:38:40-05:00","updated_at":"2026-11-10T20:00:00-05:00"},{"id":2,"title":"Lead Engineer","updated_at":"2026-11-10T20:00:00-05:00"}]}""");
        var lever = PublicBoardReader.Read("lever", """[{"id":"a","text":"Lead Engineer","createdAt":1794344400000},{"id":"b","text":"Staff Engineer"}]""");
        var ashby = PublicBoardReader.Read("ashby", """{"jobs":[{"id":"x","title":"Principal Engineer","publishedAt":"2026-11-10T18:05:00.250+00:00"},{"id":"y","title":"Staff Engineer"}]}""");

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 18, 38, 40, TimeSpan.Zero), greenhouse[0].PublishedAt);
        Assert.Equal(TimeSpan.FromHours(-5), greenhouse[0].PublishedAt!.Value.Offset);
        // Greenhouse's updated_at still dates the day when first_published is missing, and is never the instant.
        Assert.Equal((new DateOnly(2026, 11, 11), (DateTimeOffset?)null), (greenhouse[1].Posted, greenhouse[1].PublishedAt));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794344400000), lever[0].PublishedAt);
        Assert.Null(lever[1].PublishedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 18, 5, 0, 250, TimeSpan.Zero), ashby[0].PublishedAt);
        Assert.Null(ashby[1].PublishedAt);
        Assert.Equal(lever[0].PublishedAt, BoardScan.Of("lever", "acme", lever[0]).PublishedAt);
    }

    [Fact]
    public void A_lever_createdAt_outside_the_calendar_is_no_instant_and_the_rest_of_the_board_still_reads()
    {
        var lever = PublicBoardReader.Read("lever", """[{"id":"far","text":"Lead Engineer","createdAt":9223372036854775807},{"id":"before","text":"Staff Engineer","createdAt":-62135596800001},{"id":"ok","text":"Principal Engineer","createdAt":1794344400000}]""");

        Assert.Equal(["far", "before", "ok"], lever.Select(p => p.Id));
        Assert.Equal(((DateTimeOffset?)null, (DateOnly?)null), (lever[0].PublishedAt, lever[0].Posted));
        Assert.Null(lever[1].PublishedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794344400000), lever[2].PublishedAt);
    }

    [Fact]
    public void Greenhouse_has_a_light_list_without_descriptions_and_the_other_two_do_not()
    {
        Assert.Equal("https://boards-api.greenhouse.io/v1/boards/acme/jobs", PublicBoardReader.LightAddress("greenhouse", "acme"));
        Assert.Contains("content=true", PublicBoardReader.Address("greenhouse", "acme"), StringComparison.Ordinal);
        Assert.Null(PublicBoardReader.LightAddress("lever", "acme"));
        Assert.Null(PublicBoardReader.LightAddress("ashby", "acme"));
    }

    [Fact]
    public async Task Two_scans_in_a_row_make_no_second_row_and_a_moved_field_is_one_change_row()
    {
        var book = new Postings();
        var companies = new Companies(new BoardCompany(1, "greenhouse", "acme") { Key = "acme.com" });
        var boards = new Boards { ["acme"] = [Job("1", "Staff .NET Engineer", 200000), Job("2", "Senior Designer", null)] };

        string first = await new BoardScan(companies, boards, new Fixed(Night), book).RunAsync(CancellationToken.None);
        string second = await new BoardScan(companies, boards, new Fixed(Night.AddDays(1)), book).RunAsync(CancellationToken.None);

        Assert.EndsWith("postings_seen 2, postings_new 2, postings_changed 0, postings_closed 0, postings_failed 0", first, StringComparison.Ordinal);
        Assert.EndsWith("postings_seen 2, postings_new 0, postings_changed 0, postings_closed 0, postings_failed 0", second, StringComparison.Ordinal);
        Assert.Equal(2, book.Rows.Count);
        Assert.Equal(0, book.Duplicates);
        Assert.Equal(2, book.Rows.Select(r => (r.Posting.Source, r.Posting.BoardToken, r.Posting.PostingId)).Distinct().Count());
        Assert.All(book.Rows, r => Assert.Equal(Night.AddDays(1), r.LastSeen));
        Assert.Equal("acme.com", book.Rows[0].CompanyKey);
        Assert.Equal(2, book.Described.Count);

        boards["acme"] = [Job("1", "Senior .NET Engineer", 160000)];
        string third = await new BoardScan(companies, boards, new Fixed(Night.AddDays(2)), book).RunAsync(CancellationToken.None);

        Assert.EndsWith("postings_seen 1, postings_new 0, postings_changed 1, postings_closed 1, postings_failed 0", third, StringComparison.Ordinal);
        Assert.Equal([("Title", "Staff .NET Engineer", "Senior .NET Engineer"), ("Level", "Staff", "Senior"), ("CompMin", "160000", "120000"), ("CompMax", "200000", "160000")], book.Changes.Select(c => (c.Field, c.Old, c.New)));
        Assert.Equal(Night.AddDays(2), book.Rows.Single(r => r.Posting.PostingId == "2").Closed);
    }

    [Fact]
    public async Task A_new_posting_one_level_down_at_the_same_board_within_30_days_is_linked_as_a_re_post()
    {
        var book = new Postings();
        var companies = new Companies(new BoardCompany(1, "lever", "acme"));
        var boards = new Boards { ["acme"] = [Job("a", "Staff Software Engineer, Payments", 210000)] };
        await new BoardScan(companies, boards, new Fixed(Night), book).RunAsync(CancellationToken.None);
        boards["acme"] = [];
        await new BoardScan(companies, boards, new Fixed(Night.AddDays(3)), book).RunAsync(CancellationToken.None);
        boards["acme"] = [Job("b", "Senior Software Engineer, Payments", 165000)];

        await new BoardScan(companies, boards, new Fixed(Night.AddDays(9)), book).RunAsync(CancellationToken.None);

        var link = Assert.Single(book.Changes, c => c.Field == "repost_of");
        Assert.Equal((book.Rows[0].Key.ToString(System.Globalization.CultureInfo.InvariantCulture), book.Rows[1].Key.ToString(System.Globalization.CultureInfo.InvariantCulture)), (link.Old, link.New));
    }

    [Fact]
    public async Task Without_the_tables_the_scan_marks_companies_as_before_and_says_the_postings_are_blocked()
    {
        var book = new Postings { Ready = false };
        var companies = new Companies(new BoardCompany(1, "ashby", "acme"));
        string line = await new BoardScan(companies, new Boards { ["acme"] = [Job("1", "Lead .NET Engineer", null)] }, new Fixed(Night), book).RunAsync(CancellationToken.None);

        Assert.Equal("1 boards, 1 answered, 1 postings, 1 .NET, postings blocked: no_postings", line);
        Assert.Single(companies.Scanned);
        Assert.Empty(book.Rows);
    }

    [Theory]
    [InlineData("remote", "Austin, TX", null, "Remote")]
    [InlineData(null, "Remote, United States", null, "Remote")]
    [InlineData("Hybrid", "New York, NY (HQ)", true, "Hybrid")]
    [InlineData("onsite", "Boston, MA", null, "Onsite")]
    [InlineData(null, "Boston, MA", null, "Unknown")]
    public void The_remote_stance_is_read_from_the_workplace_field_and_the_location(string? workplace, string location, bool? isRemote, string stance) =>
        Assert.Equal(stance, PostingRules.Stance(workplace, location, isRemote));

    [Fact]
    public void The_scan_s_hour_is_one_o_clock_Central_under_daylight_and_standard_time()
    {
        Assert.True(CentralClock.IsScanHour(new DateTimeOffset(2026, 11, 6, 7, 0, 0, TimeSpan.Zero)));
        Assert.True(CentralClock.IsScanHour(new DateTimeOffset(2026, 10, 30, 6, 0, 0, TimeSpan.Zero)));
        Assert.False(CentralClock.IsScanHour(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void The_first_harvest_reads_as_rows_with_the_board_s_own_band()
    {
        const string text = """
            {"source": "ashby", "slug": "allocate", "company": "allocate", "title": "Sr. Software Engineer II - Full Stack", "url": "https://jobs.ashbyhq.com/allocate/68a3ac78", "location": "Palo Alto Office (Hybrid)", "remote": true, "date": "2026-08-18T22:04:43.175+00:00", "how": "body", "line": "C# APIs", "comp": "$145K – $175K"}
            {"source": "greenhouse", "slug": "acme", "company": "acme", "title": "Staff Engineer", "url": "https://job-boards.greenhouse.io/acme/jobs/77", "location": "Remote", "remote": true, "date": "2026-09-01T00:00:00Z", "how": "title", "line": ".NET", "comp": null}
            not json
            """;
        var (rows, unread) = PostingSeed.Parse(text);
        Assert.Equal(1, unread);
        Assert.Equal(("allocate", "68a3ac78", 145000, 175000, "Hybrid"), (rows[0].BoardToken, rows[0].PostingId, rows[0].CompMin, rows[0].CompMax, rows[0].RemoteStance));
        Assert.Equal(("77", (int?)null, "Remote"), (rows[1].PostingId, rows[1].CompMax, rows[1].RemoteStance));
        Assert.Equal(((int?)211400, (int?)290600), PostingSeed.Band("$211.4K - $290.6K"));
        Assert.Equal(((int?)120000, (int?)150000), PostingSeed.Band("$120,000 - $150,000"));
    }

    [Theory]
    [InlineData("&lt;p&gt;Build &amp;amp; ship C#&lt;/p&gt;", "Build & ship C#")]
    [InlineData("<div><p>Lead the  .NET\nplatform</p></div>", "Lead the .NET platform")]
    [InlineData(null, "")]
    public void A_board_s_description_is_kept_as_plain_text(string? html, string text) => Assert.Equal(text, PostingRules.Plain(html));

    [Fact]
    public void A_posting_s_hash_moves_with_its_text_and_its_band_source_says_ats_only_when_there_is_a_band()
    {
        var posting = BoardScan.Of("lever", "acme", Job("1", "Lead Engineer", 200000));
        Assert.Equal(64, posting.ContentHash.Length);
        Assert.NotEqual(posting.ContentHash, (posting with { Text = "other" }).ContentHash);
        Assert.Equal(("ats", (string?)null), (posting.CompSource, BoardScan.Of("lever", "acme", Job("2", "Lead Engineer", null)).CompSource));
    }

    private static BoardPosting Job(string id, string title, int? max) =>
        new(title, "text of " + title, new DateOnly(2026, 11, 1)) { Id = id, Url = "https://x/" + id, Location = "Remote", CompMin = max is null ? null : max - 40000, CompMax = max, CompCurrency = max is null ? null : "USD" };

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Boards : Dictionary<string, IReadOnlyList<BoardPosting>>, IBoardReader
    {
        public Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct) =>
            Task.FromResult(TryGetValue(token, out var postings) ? postings : null);
    }

    private sealed class Companies(params BoardCompany[] boards) : ICompanyBook
    {
        public List<int> Scanned { get; } = [];

        public Task<CompanyRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult<CompanyRecord?>(null);

        public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) => Task.FromResult(0);

        public Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BoardCompany>>(boards);

        public Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct)
        {
            Scanned.Add(companyId);
            return Task.CompletedTask;
        }
    }

    /// <summary>The postings tables in memory, keyed as the SQL is.</summary>
    internal sealed class Postings : IPostingBook
    {
        public bool Ready { get; init; } = true;

        public List<Row> Rows { get; } = [];

        public List<(long Key, string Field, string? Old, string? New)> Changes { get; } = [];

        public List<(long Key, string Hash)> Described { get; } = [];

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(Ready);

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(Rows.Count);

        public Task<IReadOnlyList<StoredPosting>> BoardAsync(string source, string token, DateTimeOffset closedSince, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredPosting>>([.. Rows.Where(r => r.Posting.Source == source && r.Posting.BoardToken == token && (r.Closed is null || r.Closed >= closedSince))
                .Select(r => new StoredPosting(r.Key, r.Posting.PostingId, r.Posting.Title, r.Posting.Level, r.Posting.RemoteStance, r.Posting.CompMin, r.Posting.CompMax, r.Posting.ContentHash, r.Closed))]);

        /// <summary>Rows another scan writes just after this book answers KnownAsync, so the next insert of one meets the unique key.</summary>
        public List<Posting> WrittenAfterKnown { get; } = [];

        /// <summary>Inserts the unique key refused.</summary>
        public int Duplicates { get; private set; }

        /// <summary>Updates, sightings and closes: every write that is not a new row.</summary>
        public int Touches { get; private set; }

        public Task<IReadOnlySet<string>> KnownAsync(CancellationToken ct)
        {
            IReadOnlySet<string> known = Rows.Select(r => Posting.Known(r.Posting.Source, r.Posting.BoardToken, r.Posting.PostingId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var posting in WrittenAfterKnown)
            {
                Rows.Add(new Row(Rows.Count + 1L, posting, null));
            }
            return Task.FromResult(known);
        }

        public Task<long?> AddAsync(Posting posting, string? companyKey, DateTimeOffset seenAt, CancellationToken ct)
        {
            if (Rows.Any(r => (r.Posting.Source, r.Posting.BoardToken, r.Posting.PostingId) == (posting.Source, posting.BoardToken, posting.PostingId)))
            {
                Duplicates++;
                return Task.FromResult<long?>(null);
            }
            var row = new Row(Rows.Count + 1L, posting, companyKey) { LastSeen = seenAt, FirstSeen = seenAt };
            Rows.Add(row);
            return Task.FromResult<long?>(row.Key);
        }

        public Task UpdateAsync(long key, Posting posting, DateTimeOffset at, CancellationToken ct)
        {
            Touches++;
            var row = Rows.Single(r => r.Key == key);
            (row.Posting, row.LastSeen, row.Closed) = (posting, at, null);
            return Task.CompletedTask;
        }

        public Task SeenAsync(IReadOnlyList<long> keys, DateTimeOffset at, CancellationToken ct)
        {
            Touches++;
            foreach (var row in Rows.Where(r => keys.Contains(r.Key)))
            {
                (row.LastSeen, row.Closed) = (at, null);
            }
            return Task.CompletedTask;
        }

        public Task CloseAsync(IReadOnlyList<long> keys, DateTimeOffset at, CancellationToken ct)
        {
            Touches++;
            foreach (var row in Rows.Where(r => keys.Contains(r.Key) && r.Closed is null))
            {
                row.Closed = at;
            }
            return Task.CompletedTask;
        }

        public Task ChangeAsync(long key, string field, string? oldValue, string? newValue, DateTimeOffset at, CancellationToken ct)
        {
            Changes.Add((key, field, oldValue, newValue));
            return Task.CompletedTask;
        }

        public Task DescribeAsync(long key, string hash, string text, DateTimeOffset at, CancellationToken ct)
        {
            Described.Add((key, hash));
            return Task.CompletedTask;
        }

        internal sealed class Row(long key, Posting posting, string? companyKey)
        {
            public long Key { get; } = key;

            public Posting Posting { get; set; } = posting;

            public string? CompanyKey { get; } = companyKey;

            public DateTimeOffset LastSeen { get; set; }

            public DateTimeOffset FirstSeen { get; init; }

            public DateTimeOffset? Closed { get; set; }
        }
    }
}
