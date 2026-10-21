using TheYard.Application.Features.Links;

namespace TheYard.Tests;

// #region link-handler-tests
/// <summary>
/// The Links slice without a host (ADR: Tagged links, who opened what; ADR: The onion cut into
/// vertical slices, rule 7): what a code is, which requests count as an open and which are a
/// preview fetcher, how opens fold, and the three handlers over fakes at their ports.
/// </summary>
public sealed class LinkHandlerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeTargets : ILinkTargets
    {
        public Uri? For(string key) => key == "github" ? new Uri("https://github.example/steve") : null;

        public IReadOnlyList<string> Keys => ["github"];
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData("k7f2qa", true)]
    [InlineData("abc123", true)]
    [InlineData("K7F2QA", false)]
    [InlineData("k7f2q", false)]
    [InlineData("k7f2qa7", false)]
    [InlineData("steve!", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_code_is_six_lower_case_letters_or_digits_and_nothing_else(string? value, bool isOne) =>
        Assert.Equal(isOne, LinkCodes.IsOne(value));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36", false)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", false)]
    [InlineData("LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)", true)]
    [InlineData("Mozilla/5.0 (compatible; Google-PageRenderer Google (+https://developers.google.com/+/web/snippet/))", true)]
    [InlineData("Mozilla/5.0 (Windows NT 5.1; rv:11.0) Gecko Firefox/11.0 (via ggpht.com GoogleImageProxy)", true)]
    [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)", true)]
    [InlineData("Mozilla/5.0 (compatible; Slackbot 1.0; +https://api.slack.com/robots)", true)]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", true)]
    [InlineData("Twitterbot/1.0", true)]
    [InlineData("WhatsApp/2.23.20.0", true)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SkypeUriPreview Preview/0.5 skype-url-preview@microsoft.com", true)]
    [InlineData("Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/534+ (KHTML, like Gecko) BingPreview/1.0b", true)]
    [InlineData("Microsoft Office Word 2014", true)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Safari/605.1.15 Mimecast", true)]
    [InlineData("TheYard-SelfRead/1 (deploy)", true)]
    [InlineData("curl/8.4.0", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void A_preview_fetcher_or_a_scanner_is_told_from_a_person_by_its_agent(string? agent, bool preview) =>
        Assert.Equal(preview, LinkPreviews.IsOne(agent));

    [Fact]
    public void A_tagged_get_from_a_person_is_an_open_under_its_path_and_the_page_s_report_under_its_view()
    {
        const string chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0";
        var resume = TaggedRequests.OpenOf("GET", "/resume/email", "k7f2qa", null, chrome, Noon, "sql");
        Assert.Equal(new LinkOpen(Noon, "k7f2qa", "/resume/email", "sql"), resume);
        var page = TaggedRequests.OpenOf("GET", TaggedRequests.PageReport, "k7f2qa", "/about", chrome, Noon, "cosmos");
        Assert.Equal("/about", page!.Path);
        Assert.Equal("cosmos", page.Store);
        // Not an open: a view the page does not have, a POST, no code, a code of the wrong shape, a preview fetcher.
        Assert.Null(TaggedRequests.OpenOf("GET", TaggedRequests.PageReport, "k7f2qa", "/admin", chrome, Noon, "sql"));
        Assert.Null(TaggedRequests.OpenOf("GET", TaggedRequests.PageReport, "k7f2qa", null, chrome, Noon, "sql"));
        Assert.Null(TaggedRequests.OpenOf("POST", "/resume/email", "k7f2qa", null, chrome, Noon, "sql"));
        Assert.Null(TaggedRequests.OpenOf("GET", "/resume/email", null, null, chrome, Noon, "sql"));
        Assert.Null(TaggedRequests.OpenOf("GET", "/resume/email", "Steve Stout", null, chrome, Noon, "sql"));
        Assert.Null(TaggedRequests.OpenOf("GET", "/resume/email", "k7f2qa", null, "LinkedInBot/1.0", Noon, "sql"));
        // Not an open either: a code on a path no sent link names, such as a cache-buster on an API read.
        Assert.Null(TaggedRequests.OpenOf("GET", "/api/version", "123456", null, chrome, Noon, "sql"));
        Assert.Null(TaggedRequests.OpenOf("GET", "/api/admin/plan", "k7f2qa", null, chrome, Noon, "sql"));
        Assert.NotNull(TaggedRequests.OpenOf("GET", "/api/go/github", "k7f2qa", null, chrome, Noon, "sql"));
        Assert.NotNull(TaggedRequests.OpenOf("GET", "/about", "k7f2qa", null, chrome, Noon, "sql"));
    }

    [Fact]
    public void Opens_fold_to_one_row_a_code_a_day_with_the_busiest_paths_kept()
    {
        var opens = new List<LinkOpen>
        {
            new(Noon, "k7f2qa", "/", "sql"),
            new(Noon.AddMinutes(5), "k7f2qa", "/resume/email", "sql"),
            new(Noon.AddMinutes(9), "k7f2qa", "/", "cosmos"),
            new(Noon.AddDays(1), "k7f2qa", "/api/go/github", "sql"),
            new(Noon.AddMinutes(1), "zz9zz9", "/about", "sql"),
        };
        var days = LinkOpenFolding.Fold(opens);
        Assert.Equal(3, days.Count);
        var first = days.Single(day => day.Code == "k7f2qa" && day.Day == "2026-10-20");
        Assert.Equal(3, first.Opens);
        Assert.Equal(Noon, first.First);
        Assert.Equal(Noon.AddMinutes(9), first.Last);
        Assert.Equal(2, first.Paths["/"]);
        Assert.Equal("2026-10-21", days.Single(day => day.Code == "k7f2qa" && day.Opens == 1).Day);

        var many = Enumerable.Range(0, 40).ToDictionary(i => $"/p{i}", i => i + 1, StringComparer.Ordinal);
        var merged = LinkOpenFolding.Merge(new Dictionary<string, int>(StringComparer.Ordinal) { ["/"] = 100 }, many);
        Assert.Equal(LinkOpenFolding.PathsKept, merged.Count);
        Assert.Equal(100, merged["/"]);
        Assert.Equal(40, merged["/p39"]);
    }

    [Fact]
    public async Task The_memory_keeper_adds_a_batch_to_what_it_holds_and_reads_a_window_back()
    {
        var keeper = new MemoryLinkOpens();
        await keeper.RecordAsync([new LinkOpen(Noon, "k7f2qa", "/", "sql")], CancellationToken.None);
        await keeper.RecordAsync([new LinkOpen(Noon.AddHours(2), "k7f2qa", "/resume/email", "sql"), new LinkOpen(Noon.AddDays(-10), "k7f2qa", "/", "sql")], CancellationToken.None);
        var all = await keeper.DaysAsync(Noon.AddDays(-30), CancellationToken.None);
        Assert.Equal(2, all.Count);
        var today = all.Single(day => day.Day == "2026-10-20");
        Assert.Equal(2, today.Opens);
        Assert.Equal(Noon.AddHours(2), today.Last);
        Assert.Single(await keeper.DaysAsync(Noon.AddDays(-1), CancellationToken.None));
    }

    [Fact]
    public void The_pass_through_handler_sends_a_known_key_on_and_an_unknown_one_nowhere()
    {
        var handler = new GoHandler(new FakeTargets());
        Assert.Equal(new Uri("https://github.example/steve"), handler.Handle("github"));
        Assert.Equal(new Uri("https://github.example/steve"), handler.Handle(" GitHub "));
        Assert.Null(handler.Handle("linkedin"));
        Assert.Null(handler.Handle("../admin"));
    }

    [Fact]
    public void The_page_s_report_is_taken_for_a_code_on_a_view_the_page_has_and_refused_otherwise()
    {
        var handler = new ReportPageOpenHandler();
        Assert.True(handler.Handle("k7f2qa", "/"));
        Assert.True(handler.Handle("k7f2qa", "/about"));
        Assert.False(handler.Handle("k7f2qa", "/admin"));
        Assert.False(handler.Handle("k7f2qa", null));
        Assert.False(handler.Handle("Steve", "/"));
        Assert.False(handler.Handle(null, "/"));
    }

    [Fact]
    public async Task The_card_s_handler_folds_the_kept_days_to_one_row_a_code_newest_open_first_for_the_window_asked()
    {
        var keeper = new MemoryLinkOpens();
        await keeper.RecordAsync(
            [
                new LinkOpen(Noon.AddDays(-20), "old1st", "/", "sql"),
                new LinkOpen(Noon.AddDays(-2), "k7f2qa", "/", "sql"),
                new LinkOpen(Noon.AddDays(-1), "k7f2qa", "/resume/email", "sql"),
                new LinkOpen(Noon.AddHours(-1), "zz9zz9", "/about", "cosmos"),
            ],
            CancellationToken.None);
        var handler = new ReadLinkOpensHandler(keeper, new FakeTargets(), new FakeClock(Noon));

        var month = await handler.HandleAsync("30d", CancellationToken.None);
        Assert.True(month.Available);
        Assert.Equal("30d", month.Window);
        Assert.Equal(["zz9zz9", "k7f2qa", "old1st"], month.Codes.Select(row => row.Code));
        var code = month.Codes.Single(row => row.Code == "k7f2qa");
        Assert.Equal(2, code.Opens);
        Assert.Equal(2, code.Days);
        Assert.Equal(Noon.AddDays(-2).ToUnixTimeMilliseconds(), code.FirstMs);
        Assert.Equal(1, code.Paths["/resume/email"]);
        Assert.Equal(["github"], month.Keys);

        var week = await handler.HandleAsync("7d", CancellationToken.None);
        Assert.DoesNotContain(week.Codes, row => row.Code == "old1st");
        Assert.Equal("30d", (await handler.HandleAsync("sideways", CancellationToken.None)).Window);
    }
}
// #endregion link-handler-tests
