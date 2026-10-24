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
    [InlineData("Mozilla/4.0 (compatible; ms-office; MSOffice 16)", true)]
    [InlineData("Mozilla/5.0 (Linux; Android 13; CUBOT_X30) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 12; Zoomer Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36", false)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Safari/605.1.15 Mimecast", true)]
    [InlineData("TheYard-SelfRead/1 (deploy)", true)]
    [InlineData("curl/8.4.0", true)]
    [InlineData("http.rb/5.1.1 (Mastodon/4.2.0; +https://mastodon.social/)", true)]
    [InlineData("Mozilla/5.0 (compatible; Cardyb/1.1; +mailto:support@bsky.app)", true)]
    [InlineData("axios/1.7.2", true)]
    [InlineData("okhttp/4.12.0", true)]
    [InlineData("node-fetch/1.0 (+https://github.com/bitinn/node-fetch)", true)]
    [InlineData("Dalvik/2.1.0 (Linux; U; Android 13; Pixel 7 Build/TQ3A.230805.001)", true)]
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
        // A pass-through the site refused is a guess, not a link anyone sent: a 404 never counts, a 302 does.
        Assert.Null(TaggedRequests.OpenOf("GET", "/api/go/nope", "k7f2qa", null, chrome, Noon, "sql", 404));
        Assert.NotNull(TaggedRequests.OpenOf("GET", "/api/go/github", "k7f2qa", null, chrome, Noon, "sql", 302));
    }

    /// <summary>
    /// The document store's partial updates for one day's delta: every count by increment, a path
    /// as one JSON pointer step, a new path only while the row has room, and ten operations a patch.
    /// The live account runs them only on the site; the activity container has no test copy.
    /// </summary>
    [Fact]
    public void A_stored_row_is_moved_by_increments_ten_operations_a_patch_with_paths_as_one_pointer_step()
    {
        var stored = new TheYard.Infrastructure.Cosmos.Shared.Stores.LinkOpenDocument
        {
            Opens = 11,
            FirstSeen = Noon,
            LastSeen = Noon,
            Paths = Enumerable.Range(0, 11).ToDictionary(i => $"/p{i}", _ => 1, StringComparer.Ordinal),
        };
        var delta = new LinkOpenDay("2026-10-20", "k7f2qa", Noon.AddMinutes(-1), Noon.AddMinutes(1), 4,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["/api/go/github"] = 2, ["/p3"] = 1, ["/a~b"] = 1 });

        var patches = TheYard.Infrastructure.Cosmos.Features.Links.CosmosLinkOpens.PatchesFor(stored, delta);
        var paths = patches.SelectMany(patch => patch.Select(operation => operation.Path)).ToList();

        Assert.All(patches, patch => Assert.InRange(patch.Count, 1, TheYard.Infrastructure.Cosmos.Features.Links.CosmosLinkOpens.OperationsPerPatch));
        Assert.Equal(["/opens", "/paths/~1api~1go~1github", "/paths/~1p3", "/last_seen", "/first_seen"], paths);
        Assert.All(patches.SelectMany(patch => patch).Where(operation => operation.Path.StartsWith("/paths/", StringComparison.Ordinal) || operation.Path == "/opens"),
            operation => Assert.Equal(Microsoft.Azure.Cosmos.PatchOperationType.Increment, operation.OperationType));
        Assert.Equal("~1a~0b", TheYard.Infrastructure.Cosmos.Features.Links.CosmosLinkOpens.Pointer("/a~b"));

        // Twelve new paths on an empty row make two patches: the opens and nine paths, then the rest.
        var wide = new LinkOpenDay("2026-10-20", "k7f2qa", Noon, Noon, 12, Enumerable.Range(0, 12).ToDictionary(i => $"/q{i:00}", _ => 1, StringComparer.Ordinal));
        var empty = new TheYard.Infrastructure.Cosmos.Shared.Stores.LinkOpenDocument { FirstSeen = Noon, LastSeen = Noon };
        Assert.Equal([10, 3], TheYard.Infrastructure.Cosmos.Features.Links.CosmosLinkOpens.PatchesFor(empty, wide).Select(patch => patch.Count));
    }

    /// <summary>
    /// Two containers read the same row at eleven paths and each adds a new one: the patches carry no
    /// precondition, so the row ends at thirteen. What is read back from it is twelve, the busiest,
    /// so the cap holds where it is seen.
    /// </summary>
    [Fact]
    public void Two_writers_at_eleven_paths_leave_thirteen_and_twelve_are_read_back()
    {
        var stored = new TheYard.Infrastructure.Cosmos.Shared.Stores.LinkOpenDocument
        {
            Day = "2026-10-20",
            Code = "k7f2qa",
            Opens = 11,
            FirstSeen = Noon,
            LastSeen = Noon,
            Paths = Enumerable.Range(0, 11).ToDictionary(i => $"/p{i:00}", i => 20 - i, StringComparer.Ordinal),
        };
        var row = new Dictionary<string, int>(stored.Paths, StringComparer.Ordinal);
        foreach (string added in new[] { "/w1", "/w2" })
        {
            var delta = new LinkOpenDay("2026-10-20", "k7f2qa", Noon, Noon, 1, new Dictionary<string, int>(StringComparer.Ordinal) { [added] = 1 });
            var paths = TheYard.Infrastructure.Cosmos.Features.Links.CosmosLinkOpens.PatchesFor(stored, delta)
                .SelectMany(patch => patch).Where(operation => operation.Path.StartsWith("/paths/", StringComparison.Ordinal)).ToList();
            Assert.Single(paths);
            row[added] = 1;
        }
        Assert.Equal(13, row.Count);

        var read = TheYard.Infrastructure.Cosmos.Features.Links.CosmosLinkOpens.Kept(new TheYard.Infrastructure.Cosmos.Shared.Stores.LinkOpenDocument
        {
            Day = stored.Day,
            Code = stored.Code,
            Opens = 13,
            FirstSeen = Noon,
            LastSeen = Noon,
            Paths = row,
        });

        Assert.Equal(LinkOpenFolding.PathsKept, read.Paths.Count);
        Assert.Contains("/w1", read.Paths.Keys);
        Assert.DoesNotContain("/w2", read.Paths.Keys);
        Assert.Equal(13, read.Opens);
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
        // The figure itself, not only the constant: a change to twelve is a change this test names.
        Assert.Equal(12, LinkOpenFolding.PathsKept);
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
    public async Task The_pass_through_handler_sends_a_known_key_on_and_an_unknown_one_nowhere()
    {
        var handler = new GoHandler(new FakeTargets());
        Assert.Equal(new Uri("https://github.example/steve"), await handler.HandleAsync(new GoQuery("github"), CancellationToken.None));
        Assert.Equal(new Uri("https://github.example/steve"), await handler.HandleAsync(new GoQuery(" GitHub "), CancellationToken.None));
        Assert.Null(await handler.HandleAsync(new GoQuery("linkedin"), CancellationToken.None));
        Assert.Null(await handler.HandleAsync(new GoQuery("../admin"), CancellationToken.None));
    }

    [Fact]
    public async Task The_page_s_report_is_taken_for_a_code_on_a_view_the_page_has_and_refused_otherwise()
    {
        var handler = new ReportPageOpenHandler();
        Assert.True(await handler.HandleAsync(new ReportPageOpenCommand("k7f2qa", "/"), CancellationToken.None));
        Assert.True(await handler.HandleAsync(new ReportPageOpenCommand("k7f2qa", "/about"), CancellationToken.None));
        Assert.False(await handler.HandleAsync(new ReportPageOpenCommand("k7f2qa", "/admin"), CancellationToken.None));
        Assert.False(await handler.HandleAsync(new ReportPageOpenCommand("k7f2qa", null), CancellationToken.None));
        Assert.False(await handler.HandleAsync(new ReportPageOpenCommand("Steve", "/"), CancellationToken.None));
        Assert.False(await handler.HandleAsync(new ReportPageOpenCommand(null, "/"), CancellationToken.None));
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

        var month = await handler.HandleAsync(new ReadLinkOpensQuery("30d"), CancellationToken.None);
        Assert.True(month.Available);
        Assert.Equal("30d", month.Window);
        Assert.Equal(["zz9zz9", "k7f2qa", "old1st"], month.Codes.Select(row => row.Code));
        var code = month.Codes.Single(row => row.Code == "k7f2qa");
        Assert.Equal(2, code.Opens);
        Assert.Equal(2, code.Days);
        Assert.Equal(Noon.AddDays(-2).ToUnixTimeMilliseconds(), code.FirstMs);
        Assert.Equal(1, code.Paths["/resume/email"]);
        Assert.Equal(["github"], month.Keys);

        var week = await handler.HandleAsync(new ReadLinkOpensQuery("7d"), CancellationToken.None);
        Assert.DoesNotContain(week.Codes, row => row.Code == "old1st");
        Assert.Equal("30d", (await handler.HandleAsync(new ReadLinkOpensQuery("sideways"), CancellationToken.None)).Window);
    }
}
// #endregion link-handler-tests
