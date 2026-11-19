using JobHunt.Application.Shared.Walls;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The finder pass (ADR-118): a finder's answer is held to the public-page rules before it is written, the month's
/// cap stops the asking, a lead is asked once a month, a call that throws is still counted, and with no key (or
/// "off") the Hunter adapter is never built. Hunter's JSON is kept only with a
/// score of 70 or more, a valid mark and a public page it cites.
/// </summary>
public sealed class AddressSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 15, 20, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_answer_is_written_only_on_the_company_s_domain_with_a_page_and_mail()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"), new WaitingLead(3, "acme.com", "Jo Ray"), new WaitingLead(4, "globex.com", "Bo Kay"));
        var finder = new Finder(new()
        {
            ["Pat Lee"] = new FoundAddress("pat@acme.com", "https://acme.com/team"),
            ["Sam Roe"] = new FoundAddress("sam@gmail.com", "https://acme.com/team"),
            ["Jo Ray"] = new FoundAddress("jo@other.com", "https://acme.com/team"),
            ["Bo Kay"] = new FoundAddress("bo@globex.com", "http://globex.com/about"),
        });
        var (search, root) = Search(book, finder, 50);

        string line = await search.RunAsync(CancellationToken.None);

        Assert.Equal("fake: 4 asked, 1 found, 3 dropped, 4 of 50 this month", line);
        Assert.Equal([(1L, "pat@acme.com", "https://acme.com/team")], book.Written);
        Assert.Equal("4", File.ReadAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10.txt")));
    }

    [Fact]
    public async Task The_month_s_cap_stops_the_asking()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"));
        var (search, root) = Search(book, new Finder([]), 50);
        Directory.CreateDirectory(Path.Combine(root, "campaign", "finder"));
        File.WriteAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10.txt"), "49");

        Assert.Equal("fake: 1 asked, 0 found, 0 dropped, 50 of 50 this month", await search.RunAsync(CancellationToken.None));
        Assert.Equal("fake: 0 asked, 0 found, 0 dropped, 50 of 50 this month", await search.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_lead_asked_this_month_is_not_asked_again()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"));
        var finder = new Finder([]);
        var (search, root) = Search(book, finder, 50);

        Assert.Equal("fake: 2 asked, 0 found, 0 dropped, 2 of 50 this month", await search.RunAsync(CancellationToken.None));
        Assert.Equal("fake: 0 asked, 0 found, 0 dropped, 2 of 50 this month", await search.RunAsync(CancellationToken.None));
        Assert.Equal(2, finder.Calls);
        Assert.Equal("1\n2\n", File.ReadAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10-asked.txt")));
    }

    // ADR-118 addendum 11/12: a call that throws stays counted (Hunter may have charged it); a refusal does not.
    [Fact]
    public async Task A_call_that_throws_is_still_counted_against_the_cap()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"), new WaitingLead(3, "acme.com", "Jo Ray"));
        var (search, root) = Search(book, new Finder([]) { Throws = "Sam Roe" }, 50);

        await Assert.ThrowsAsync<HttpRequestException>(() => search.RunAsync(CancellationToken.None));

        Assert.Equal("2", File.ReadAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10.txt")));
        Assert.Equal("1\n2\n", File.ReadAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10-asked.txt")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("off")]
    [InlineData(" OFF ")]
    public void With_no_key_the_hunter_finder_is_never_built(string? key) =>
        Assert.Null(HunterFinder.FromSetting(key, new NoHttp()));

    [Fact]
    public void Hunter_s_answer_needs_a_score_a_valid_mark_and_a_page()
    {
        const string Good = """{"data":{"email":"pat@acme.com","score":91,"verification":{"status":"valid"},"sources":[{"uri":"https://acme.com/team"}]}}""";
        Assert.Equal(new FoundAddress("pat@acme.com", "https://acme.com/team"), HunterFinder.Read(Good));
        Assert.Null(HunterFinder.Read(Good.Replace("91", "69", StringComparison.Ordinal)));
        Assert.Null(HunterFinder.Read(Good.Replace("\"valid\"", "\"accept_all\"", StringComparison.Ordinal)));
        Assert.Null(HunterFinder.Read("""{"data":{"email":"pat@acme.com","score":95,"verification":{"status":"accept_all"},"sources":[]}}"""));
        Assert.Null(HunterFinder.Read("""{"data":null}"""));
    }

    // Steve 11/12 08:42 CST: a Hunter "valid" address counts with no cited page; its source names Hunter's verifier, as hunter_find.py writes it.
    [Fact]
    public void A_valid_answer_with_no_page_is_kept_with_the_verifier_as_its_source()
    {
        Assert.Equal(
            new FoundAddress("pat@acme.com", HunterFinder.VerifierPage),
            HunterFinder.Read("""{"data":{"email":"pat@acme.com","score":95,"verification":{"status":"valid"},"sources":[]}}"""));
        Assert.Null(AddressSearch.Refuse(new FoundAddress("pat@acme.com", HunterFinder.VerifierPage), "acme.com"));
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData("", 50)]
    [InlineData("1500", 1500)]
    [InlineData(" 1500 ", 1500)]
    [InlineData("many", 50)]
    [InlineData("-3", 50)]
    [InlineData("0", 0)]
    public void The_month_s_cap_is_the_setting_and_the_free_plan_s_50_without_one(string? setting, int cap) =>
        Assert.Equal(cap, HunterFinder.MonthCapFrom(setting));

    [Fact]
    public void The_searches_left_are_read_from_the_account_answer()
    {
        Assert.Equal(1988, HunterFinder.SearchesLeft("""{"data":{"plan_name":"Starter","requests":{"searches":{"used":12,"available":2000,"remaining":1988}}}}"""));
        Assert.Equal(1990, HunterFinder.SearchesLeft("""{"data":{"requests":{"searches":{"used":10,"available":2000}}}}"""));
        Assert.Null(HunterFinder.SearchesLeft("""{"data":{"requests":{}}}"""));
        Assert.Null(HunterFinder.SearchesLeft("not json"));
    }

    [Fact]
    public async Task The_plan_is_read_from_hunter_s_account_endpoint_and_a_failure_reads_as_unknown()
    {
        var ok = new OneAnswer(System.Net.HttpStatusCode.OK, """{"data":{"requests":{"searches":{"used":0,"available":2000,"remaining":2000}}}}""");
        Assert.Equal(2000, await new HunterFinder(ok, "k").SearchesLeftAsync(CancellationToken.None));
        Assert.StartsWith("https://api.hunter.io/v2/account?api_key=", ok.Asked, StringComparison.Ordinal);
        Assert.Null(await new HunterFinder(new OneAnswer(System.Net.HttpStatusCode.Unauthorized, "{}"), "k").SearchesLeftAsync(CancellationToken.None));
    }

    private sealed class OneAnswer(System.Net.HttpStatusCode status, string body) : HttpMessageHandler, IHttpClientFactory
    {
        public string? Asked { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    // HUNTER1119: on the paid plan the cap is a setting, and each pass also stops at what the plan reports left.
    [Fact]
    public async Task The_pass_stops_at_the_lower_of_the_cap_and_the_searches_the_plan_has_left()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"), new WaitingLead(3, "acme.com", "Jo Ray"));
        var finder = new Finder([]);
        var (search, _) = Search(book, finder, 1500, new Plan(2));

        Assert.Equal("fake: 2 asked, 0 found, 0 dropped, 2 of 1500 this month, 0 left on the plan", await search.RunAsync(CancellationToken.None));
        Assert.Equal(2, finder.Calls);
    }

    [Fact]
    public async Task A_plan_with_nothing_left_asks_nothing()
    {
        var finder = new Finder([]);
        var (search, root) = Search(new Book(new WaitingLead(1, "acme.com", "Pat Lee")), finder, 1500, new Plan(0));

        Assert.Equal("fake: 0 asked, 0 found, 0 dropped, 0 of 1500 this month, 0 left on the plan", await search.RunAsync(CancellationToken.None));
        Assert.Equal(0, finder.Calls);
        Assert.False(File.Exists(Path.Combine(root, "campaign", "finder", "fake-2026-10.txt")));
    }

    [Fact]
    public async Task A_plan_that_does_not_answer_leaves_the_cap_in_charge()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"));
        var (search, _) = Search(book, new Finder([]), 1500, new Plan(null));

        Assert.Equal("fake: 2 asked, 0 found, 0 dropped, 2 of 1500 this month, plan not read", await search.RunAsync(CancellationToken.None));
    }

    private static (AddressSearch Search, string Root) Search(Book book, IAddressFinder finder, int cap, IFinderPlan? plan = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "address-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (new AddressSearch(book, finder, new Mx(), new FolderMergeStore(root), new FixedTime(Now), cap) { Plan = plan }, root);
    }

    private sealed class Plan(int? left) : IFinderPlan
    {
        public Task<int?> SearchesLeftAsync(CancellationToken ct) => Task.FromResult(left);
    }

    [Fact]
    public async Task A_refused_call_is_not_counted_and_its_lead_is_asked_again()
    {
        var book = new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe"), new WaitingLead(3, "acme.com", "Jo Ray"));
        var (search, root) = Search(book, new Finder([]) { Refuses = "Sam Roe" }, 50);

        string line = await search.RunAsync(CancellationToken.None);

        Assert.Equal("fake: 1 asked, 0 found, 0 dropped, 1 of 50 this month, 1 refused (HTTP 429 restricted_account)", line);
        Assert.Equal("1", File.ReadAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10.txt")));
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(root, "campaign", "finder", "fake-2026-10-asked.txt")));
    }

    [Fact]
    public async Task Ten_asked_and_none_found_stops_the_asking_with_one_wall_and_a_found_address_clears_it()
    {
        var leads = Enumerable.Range(1, 14).Select(i => new WaitingLead(i, "acme.com", $"Person {i}")).ToArray();
        var book = new Book(leads);
        var finder = new Finder([]);
        var clock = new Clock(Now);
        string root = Path.Combine(Path.GetTempPath(), "address-floor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AddressSearch Search() => new(book, finder, new Mx(), new FolderMergeStore(root), clock, 50) { FloorControl = "read one lookup by hand at hunter.io" };

        string first = await Search().RunAsync(CancellationToken.None);
        Assert.StartsWith("fake: 10 asked, 0 found, 0 dropped, 10 of 50 this month, stopped: 10 asked and none found", first, StringComparison.Ordinal);
        Assert.Equal(10, finder.Calls);
        string walls = File.ReadAllText(Path.Combine(root, "dispatch", "WALLS.md"));
        Assert.Contains("read one lookup by hand at hunter.io", walls, StringComparison.Ordinal);

        clock.At = Now.AddHours(23);
        Assert.Contains("0 asked", await Search().RunAsync(CancellationToken.None), StringComparison.Ordinal);
        Assert.Equal(10, finder.Calls);

        clock.At = Now.AddHours(25);
        finder.Answers["Person 11"] = new FoundAddress("p11@acme.com", "https://acme.com/team");
        Assert.StartsWith("fake: 1 asked, 1 found", await Search().RunAsync(CancellationToken.None), StringComparison.Ordinal);
        Assert.Equal(11, finder.Calls);
        Assert.DoesNotContain("wall:fake-empty", File.ReadAllText(Path.Combine(root, "dispatch", "WALLS.md")), StringComparison.Ordinal);
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        public DateTimeOffset At { get; set; } = at;

        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class Book(params WaitingLead[] waiting) : IAddressBook
    {
        public List<(long, string, string)> Written { get; } = [];

        public Task<int> WithAddressAsync(IReadOnlyCollection<long> leadIds, CancellationToken ct) =>
            Task.FromResult(Written.Count(w => leadIds.Contains(w.Item1)));

        public Task<IReadOnlyList<WaitingLead>> WaitingAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<WaitingLead>>(waiting);

        public Task SetAddressAsync(long leadId, string address, string sourceUrl, bool? mx, DateTimeOffset at, CancellationToken ct)
        {
            Written.Add((leadId, address, sourceUrl));
            return Task.CompletedTask;
        }
    }

    private sealed class Finder(Dictionary<string, FoundAddress> answers) : IAddressFinder
    {
        public string Name => "fake";

        public Dictionary<string, FoundAddress> Answers { get; } = answers;

        public int Calls { get; private set; }

        public string? Throws { get; init; }

        public string? Refuses { get; init; }

        public Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct)
        {
            Calls++;
            return fullName == Throws ? throw new HttpRequestException("the finder failed")
                : fullName == Refuses ? throw new FinderRefusedException(Name, "HTTP 429 restricted_account", called: true)
                : Task.FromResult(Answers.TryGetValue(fullName, out var a) ? a : null);
        }
    }

    private sealed class Mx : IMxCheck
    {
        public Task<bool?> HasMxAsync(string domain, CancellationToken ct) => Task.FromResult<bool?>(true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no call is made");
    }
}
