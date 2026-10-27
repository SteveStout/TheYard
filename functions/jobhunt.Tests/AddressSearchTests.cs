using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The finder pass (ADR-118): a finder's answer is held to the public-page rules before it is written, the month's
/// cap stops the asking, and with no key the Hunter adapter is never built. Hunter's JSON is kept only with a
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void With_no_key_the_hunter_finder_is_never_built(string? key) =>
        Assert.Null(HunterFinder.FromSetting(key, new NoHttp()));

    [Fact]
    public void Hunter_s_answer_needs_a_score_a_valid_mark_and_a_page()
    {
        const string Good = """{"data":{"email":"pat@acme.com","score":91,"verification":{"status":"valid"},"sources":[{"uri":"https://acme.com/team"}]}}""";
        Assert.Equal(new FoundAddress("pat@acme.com", "https://acme.com/team"), HunterFinder.Read(Good));
        Assert.Null(HunterFinder.Read(Good.Replace("91", "69", StringComparison.Ordinal)));
        Assert.Null(HunterFinder.Read(Good.Replace("\"valid\"", "\"accept_all\"", StringComparison.Ordinal)));
        Assert.Null(HunterFinder.Read("""{"data":{"email":"pat@acme.com","score":95,"verification":{"status":"valid"},"sources":[]}}"""));
        Assert.Null(HunterFinder.Read("""{"data":null}"""));
    }

    private static (AddressSearch Search, string Root) Search(Book book, IAddressFinder finder, int cap)
    {
        string root = Path.Combine(Path.GetTempPath(), "address-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (new AddressSearch(book, finder, new Mx(), new FolderMergeStore(root), new FixedTime(Now), cap), root);
    }

    private sealed class Book(params WaitingLead[] waiting) : IAddressBook
    {
        public List<(long, string, string)> Written { get; } = [];

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

        public Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct) =>
            Task.FromResult(answers.TryGetValue(fullName, out var a) ? a : null);
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
