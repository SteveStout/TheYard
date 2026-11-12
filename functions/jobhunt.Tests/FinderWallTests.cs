using System.Net;
using System.Text;
using JobHunt.Application.Features.Campaign;
using JobHunt.Application.Shared.Walls;
using JobHunt.Domain.Shared.Walls;
using JobHunt.Infrastructure.Features.Campaign;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// jhcode1112 1b: Hunter's refusals are a wall, not "no address found". HunterFinder answered null on any
/// non-success, so 21 calls on 11/12 met HTTP 429 restricted_account and read as 21 people with no public address.
/// </summary>
public sealed class FinderWallTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 16, 20, 0, TimeSpan.Zero);

    private const string Restricted = """{"errors":[{"id":"restricted_account","code":429,"details":"Your account has been restricted."}]}""";

    private const string Walls = """
        > RULES FIRST: read systemRules.md before acting on this file.

        # WALLS: what only Steve can clear

        One bullet a wall.


        - none (HUNTER_KEY cleared 08:28 CST Tue 11/10)
        """;

    [Theory]
    [InlineData(429, Restricted, "HTTP 429 restricted_account")]
    [InlineData(401, "", "HTTP 401")]
    [InlineData(403, """{"errors":[{"id":"forbidden"}]}""", "HTTP 403 forbidden")]
    public void Restricted_and_auth_statuses_are_refusals(int status, string body, string reason) =>
        Assert.Equal(reason, HunterFinder.Refused(status, body));

    [Theory]
    [InlineData(429, """{"errors":[{"id":"too_many_requests"}]}""")]
    [InlineData(500, "")]
    [InlineData(404, "")]
    public void Other_failures_stay_no_answer(int status, string body) => Assert.Null(HunterFinder.Refused(status, body));

    [Fact]
    public async Task A_restricted_account_throws_a_named_refusal_not_null()
    {
        var hunter = new HunterFinder(new Answers(HttpStatusCode.TooManyRequests, Restricted), "key");

        var refused = await Assert.ThrowsAsync<FinderRefusedException>(() => hunter.FindAsync("Pat Lee", "acme.com", CancellationToken.None));

        Assert.True(refused.Called);
        Assert.Equal("HTTP 429 restricted_account", refused.Reason);
    }

    [Fact]
    public void A_wall_replaces_none_and_keeps_its_first_time_when_refreshed()
    {
        string first = WallList.With(Walls, "hunter-refused", "Hunter refused the finder.", Now);
        Assert.DoesNotContain("- none", first, StringComparison.Ordinal);
        Assert.Equal(Now, WallList.Last(first, "hunter-refused"));

        string again = WallList.With(first, "hunter-refused", "A different text.", Now.AddHours(25));
        Assert.Contains("Hunter refused the finder.", again, StringComparison.Ordinal);
        Assert.DoesNotContain("A different text.", again, StringComparison.Ordinal);
        Assert.Equal(Now.AddHours(25), WallList.Last(again, "hunter-refused"));
        Assert.Single(again.Split('\n'), l => l.Contains("wall:hunter-refused", StringComparison.Ordinal));

        string gone = WallList.Without(again, "hunter-refused");
        Assert.Null(WallList.Last(gone, "hunter-refused"));
        Assert.Contains("\n- none", gone, StringComparison.Ordinal);
        Assert.StartsWith("> RULES FIRST", gone, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_one_wall_keeps_the_others()
    {
        string two = WallList.With(WallList.With(Walls, "a", "Wall A.", Now), "b", "Wall B.", Now);
        string one = WallList.Without(two, "a");
        Assert.Contains("Wall B.", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Wall A.", one, StringComparison.Ordinal);
        Assert.DoesNotContain("- none", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_writes_the_bullet_and_stops_every_call_for_24_hours()
    {
        var (store, root) = Store();
        var inner = new Finder { Refuse = true };
        var clock = new Clock(Now);

        await Assert.ThrowsAsync<FinderRefusedException>(() => new WalledFinder(inner, store, clock, HunterFinder.Control).FindAsync("Pat Lee", "acme.com", CancellationToken.None));
        string walls = File.ReadAllText(Path.Combine(root, "dispatch", "WALLS.md"));
        Assert.Contains("sign in at hunter.io and read the account notice", walls, StringComparison.Ordinal);
        Assert.Contains("HTTP 429 restricted_account", walls, StringComparison.Ordinal);
        Assert.Contains("Since 10:20 CST Thu 11/12", walls, StringComparison.Ordinal);

        clock.At = Now.AddHours(23);
        var stopped = await Assert.ThrowsAsync<FinderRefusedException>(() => new WalledFinder(inner, store, clock, HunterFinder.Control).FindAsync("Sam Roe", "acme.com", CancellationToken.None));
        Assert.False(stopped.Called);
        Assert.Equal(1, inner.Calls);

        clock.At = Now.AddHours(25);
        inner.Refuse = false;
        Assert.NotNull(await new WalledFinder(inner, store, clock, HunterFinder.Control).FindAsync("Sam Roe", "acme.com", CancellationToken.None));
        Assert.Equal(2, inner.Calls);
        Assert.DoesNotContain("wall:hunter-refused", File.ReadAllText(Path.Combine(root, "dispatch", "WALLS.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lead_the_wall_stopped_before_the_call_is_not_counted_or_locked_out()
    {
        var (store, root) = Store();
        string walled = WallList.With(Walls, "hunter-refused", "Hunter refused the finder.", Now.AddHours(-1));
        File.WriteAllText(Path.Combine(root, "dispatch", "WALLS.md"), walled);
        var inner = new Finder();
        var search = new AddressSearch(new Book(new WaitingLead(1, "acme.com", "Pat Lee"), new WaitingLead(2, "acme.com", "Sam Roe")),
            new WalledFinder(inner, store, new Clock(Now), HunterFinder.Control), new Mx(), store, new Clock(Now), 50);

        string line = await search.RunAsync(CancellationToken.None);

        Assert.Equal(0, inner.Calls);
        Assert.Contains("stopped: walled until", line, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "campaign", "finder", "hunter-2026-11-asked.txt")));
    }

    private static (FolderMergeStore Store, string Root) Store()
    {
        string root = Path.Combine(Path.GetTempPath(), "finder-wall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "dispatch"));
        File.WriteAllText(Path.Combine(root, "dispatch", "WALLS.md"), Walls);
        return (new FolderMergeStore(root), root);
    }

    private sealed class Finder : IAddressFinder
    {
        public string Name => "hunter";

        public bool Refuse { get; set; }

        public int Calls { get; private set; }

        public Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct)
        {
            Calls++;
            return Refuse ? throw new FinderRefusedException("hunter", "HTTP 429 restricted_account", called: true)
                : Task.FromResult<FoundAddress?>(new FoundAddress("pat@acme.com", "https://acme.com/team"));
        }
    }

    private sealed class Book(params WaitingLead[] waiting) : IAddressBook
    {
        public Task<IReadOnlyList<WaitingLead>> WaitingAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<WaitingLead>>(waiting);

        public Task SetAddressAsync(long leadId, string address, string sourceUrl, bool? mx, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<int> WithAddressAsync(IReadOnlyCollection<long> leadIds, CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class Mx : IMxCheck
    {
        public Task<bool?> HasMxAsync(string domain, CancellationToken ct) => Task.FromResult<bool?>(true);
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        public DateTimeOffset At { get; set; } = at;

        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class Answers(HttpStatusCode status, string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(status, body));

        private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
