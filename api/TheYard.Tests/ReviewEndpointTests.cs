using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Review;

namespace TheYard.Tests;

/// <summary>
/// The review route's two routes through the real host (ADR: Ten minutes, seven stops, the addendum on the
/// reach rows): a stop on the route is 204 and a stop off it is 400 with the problem, the card counts the
/// visitor once at the highest stop with the code beside it, a lower stop never lowers the row, and a scanner
/// is answered and never counted. The host here has no document store, so the reach is kept in memory.
/// </summary>
public sealed class ReviewEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    private HttpClient As(string agent)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
        return client;
    }

    private Task Drain() => factory.Services.GetRequiredService<ReviewReachCollector>().DrainAsync(CancellationToken.None);

    [Fact]
    [Covers("POST", "/api/review/reached", 204)]
    [Covers("POST", "/api/review/reached", 400)]
    [Covers("GET", "/api/admin/review", 200)]
    public async Task A_reach_is_kept_once_at_the_highest_stop_and_the_card_reads_it_back_with_its_code()
    {
        // The codes are this test's own, so a row another run or another class kept never decides it: the
        // suite boots on each store, and on the document store the activity container outlives the run.
        string code = "t" + Guid.NewGuid().ToString("N")[..5];
        string scanned = "s" + Guid.NewGuid().ToString("N")[..5];
        // And the visitor is this test's own: the test host gives every request one empty address, so the hash
        // would be the same every run, and a row an earlier run kept today holds its first code. The person hash
        // drops digits from the agent, so the mark is letters, from ones that spell no scanner's word.
        string mark = string.Concat(Guid.NewGuid().ToString("N").Select(hex => "ghjkmnpqrsuvwxyz"[Convert.ToInt32(hex.ToString(), 16)]));
        var person = As($"{Chrome} Visitor/{mark}");
        Assert.Equal(HttpStatusCode.NoContent, (await person.PostAsJsonAsync("/api/review/reached", new { stop = 0, r = code })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await person.PostAsJsonAsync("/api/review/reached", new { stop = 3, r = code })).StatusCode);
        await Problem.Is(await person.PostAsJsonAsync("/api/review/reached", new { stop = 8, r = code }), HttpStatusCode.BadRequest);
        // A scanner is answered and never counted.
        Assert.Equal(HttpStatusCode.NoContent, (await As("curl/8.5.0").PostAsJsonAsync("/api/review/reached", new { stop = 7, r = scanned })).StatusCode);
        await Drain();
        // A lower stop for the same browser leaves the row where it was.
        Assert.Equal(HttpStatusCode.NoContent, (await person.PostAsJsonAsync("/api/review/reached", new { stop = 1, r = code })).StatusCode);
        await Drain();

        var card = await person.GetAsync("/api/admin/review?window=7d");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        using var funnel = JsonDocument.Parse(await card.Content.ReadAsStringAsync());
        var root = funnel.RootElement;
        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.Equal("7d", root.GetProperty("window").GetString());
        Assert.True(root.GetProperty("opened").GetInt32() >= 1);
        var reached = root.GetProperty("reached_by_stop").EnumerateArray().Select(count => count.GetInt32()).ToList();
        Assert.Equal(8, reached.Count);
        Assert.True(reached[3] >= 1);
        Assert.True(reached.Zip(reached.Skip(1)).All(pair => pair.First >= pair.Second), "a later stop never has more visitors than an earlier one");
        var row = root.GetProperty("by_code").EnumerateArray().Single(entry => entry.GetProperty("code").GetString() == code);
        Assert.Equal(3, row.GetProperty("stop").GetInt32());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", root.GetProperty("counted_from").GetString()!);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("source").GetString()));
        Assert.DoesNotContain(scanned, root.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_window_reads_the_week_and_the_four_windows_answer()
    {
        var client = As(Chrome);
        foreach (string window in new[] { "24h", "7d", "30d", "90d" })
        {
            using var answer = JsonDocument.Parse(await client.GetStringAsync($"/api/admin/review?window={window}"));
            Assert.Equal(window, answer.RootElement.GetProperty("window").GetString());
            Assert.Equal(8, answer.RootElement.GetProperty("reached_by_stop").GetArrayLength());
        }
        using var unknown = JsonDocument.Parse(await client.GetStringAsync("/api/admin/review?window=1y"));
        Assert.Equal("7d", unknown.RootElement.GetProperty("window").GetString());
    }
}
