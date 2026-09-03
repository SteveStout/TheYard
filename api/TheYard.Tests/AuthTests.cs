using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// Accounts, and the bids that belong to them (ADR: Accounts and per-user
/// bids). The auction was one anonymous buyer until this file existed, which
/// made "you are the high bidder" a statement about the only person in the
/// room.
///
/// These tests share one database file on purpose: the interesting claims are
/// about two people and about a restart, and neither can be made by one
/// process with a scratch database.
/// </summary>
public class AuthTests : IDisposable
{
    private readonly string _file =
        Path.Combine(Path.GetTempPath(), $"theyard-auth-{Guid.NewGuid():N}.db");

    private string Connection => $"Data Source={_file};Pooling=False";

    private WebApplicationFactory<Program> Api() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Yard", Connection);
            // A fixed key, so a token minted by one process is still valid in
            // the next one. Without it every restart invents a new key and the
            // restart test would be measuring the key rather than the bids.
            builder.UseSetting("Auth:SigningKey", "a-signing-key-for-tests-only-not-a-secret");
        });

    private static async Task<HttpResponseMessage> Register(HttpClient client, string email) =>
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "correct horse" });

    private static async Task<HttpResponseMessage> LogIn(HttpClient client, string email) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" });

    private static long Anchor() =>
        new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>
    /// Everything a rejected sign-in tells the caller, as one comparable
    /// string. Two request identifiers are left out and nothing else is: the
    /// W3C `trace_id` the telemetry attaches (ADR: Observability) and the
    /// `traceId` ProblemDetails adds of its own accord. Both are a fresh value
    /// on every response of every kind, and neither says anything about the
    /// account. Everything else has to match, which is a stronger claim than
    /// comparing a chosen few fields would be.
    /// </summary>
    private static readonly string[] RequestIds = ["trace_id", "traceId"];

    private static async Task<string> Told(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join(
            "&",
            body.RootElement.EnumerateObject()
                .Where(property => !RequestIds.Contains(property.Name))
                .Select(property => $"{property.Name}={property.Value}")
                .Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A bid the server accepted, or a failure that says why it did not. Every
    /// rejection carries its sentence in `detail` (ADR: Error handling).
    /// </summary>
    private static async Task<HttpResponseMessage> Bid(
        HttpClient client, string vehicleId, int amount, long anchor)
    {
        var placed = await client.PostAsJsonAsync(
            $"/api/vehicles/{vehicleId}/bids", new { amount, anchor_ms = anchor });
        Assert.True(
            placed.IsSuccessStatusCode,
            $"the bid of {amount} on {vehicleId} was refused: "
                + await placed.Content.ReadAsStringAsync());
        return placed;
    }

    /// <summary>
    /// A live vehicle with time left on it. Sorted by most bids rather than
    /// the default, which is EndingSoonest and therefore hands back the one
    /// auction in the dataset with the least time on it. AuctionClock's NowMs
    /// is real wall-clock read per request, so that vehicle can close between
    /// the read and the bid, and under the full suite it does.
    /// </summary>
    private static async Task<(string Id, int MinNext)> ALiveVehicle(HttpClient client, long anchor)
    {
        using var page = JsonDocument.Parse(
            await client.GetStringAsync(
                $"/api/vehicles?status=live&sort=most-bids&limit=1&anchor_ms={anchor}"));
        var live = page.RootElement.GetProperty("vehicles");
        Assert.True(live.GetArrayLength() > 0, "the anchored clock should always have a live auction");
        var vehicle = live[0];
        return (vehicle.GetProperty("id").GetString()!, vehicle.GetProperty("min_next_bid").GetInt32());
    }

    // #region auth-tests
    [Fact]
    public async Task Registering_signs_you_in_and_never_hands_the_page_the_token()
    {
        await using var api = Api();
        var client = api.CreateClient();

        var response = await Register(client, "first@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("first@example.com", body, StringComparison.Ordinal);

        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        // httpOnly is the whole reason the token is in a cookie rather than in
        // the response: a page that can read its own token can leak it.
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bidding_without_an_account_is_refused()
    {
        await using var api = Api();
        var client = api.CreateClient();
        long anchor = Anchor();
        var (id, amount) = await ALiveVehicle(client, anchor);

        var bid = await client.PostAsJsonAsync(
            $"/api/vehicles/{id}/bids", new { amount, anchor_ms = anchor });
        var buyNow = await client.PostAsJsonAsync(
            $"/api/vehicles/{id}/buy-now", new { anchor_ms = anchor });
        var reset = await client.DeleteAsync("/api/bids");
        var history = await client.GetAsync("/api/bids/history");

        Assert.Equal(HttpStatusCode.Unauthorized, bid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, buyNow.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, history.StatusCode);

        // Reading is still open. An auction nobody can watch without signing up
        // is a worse demo and no safer.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bids")).StatusCode);
        Assert.Equal("{}", (await client.GetStringAsync("/api/bids")).Trim());
    }

    [Fact]
    public async Task A_second_account_outbids_the_first_and_both_are_told_the_truth()
    {
        await using var api = Api();
        long anchor = Anchor();

        var first = api.CreateClient();
        await Register(first, "first@example.com");
        var (id, opening) = await ALiveVehicle(first, anchor);
        await Bid(first, id, opening, anchor);

        // A different client is a different browser: its own cookie jar, its
        // own account.
        var second = api.CreateClient();
        await Register(second, "second@example.com");
        using var detail = JsonDocument.Parse(
            await second.GetStringAsync($"/api/vehicles/{id}?anchor_ms={anchor}"));
        int nextUp = detail.RootElement.GetProperty("min_next_bid").GetInt32();
        Assert.True(nextUp > opening, "the second account has to clear the first");
        await Bid(second, id, nextUp, anchor);

        using var mineFirst = JsonDocument.Parse(await first.GetStringAsync("/api/bids"));
        using var mineSecond = JsonDocument.Parse(await second.GetStringAsync("/api/bids"));

        var one = mineFirst.RootElement.GetProperty(id);
        var two = mineSecond.RootElement.GetProperty(id);
        Assert.True(one.GetProperty("outbid").GetBoolean(), "the first account has been outbid");
        Assert.False(two.GetProperty("outbid").GetBoolean(), "the second account holds it");
        Assert.Equal(opening, one.GetProperty("amount").GetInt32());
        Assert.Equal(nextUp, two.GetProperty("amount").GetInt32());
        // Both are told the same thing about the vehicle, whatever they hold.
        Assert.Equal(nextUp, one.GetProperty("highest_amount").GetInt32());
        Assert.Equal(nextUp, two.GetProperty("highest_amount").GetInt32());

        // And each sees only their own.
        Assert.Single(mineFirst.RootElement.EnumerateObject());
        Assert.Single(mineSecond.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Both_accounts_and_both_bids_survive_a_restart()
    {
        long anchor = Anchor();
        string id;
        int firstAmount;
        int secondAmount;

        await using (var api = Api())
        {
            var first = api.CreateClient();
            await Register(first, "first@example.com");
            (id, firstAmount) = await ALiveVehicle(first, anchor);
            await Bid(first, id, firstAmount, anchor);

            var second = api.CreateClient();
            await Register(second, "second@example.com");
            using var detail = JsonDocument.Parse(
                await second.GetStringAsync($"/api/vehicles/{id}?anchor_ms={anchor}"));
            secondAmount = detail.RootElement.GetProperty("min_next_bid").GetInt32();
            await Bid(second, id, secondAmount, anchor);
        }

        await using var restarted = Api();
        var backAsFirst = restarted.CreateClient();
        // The account outlived the process, so signing in again is a login and
        // not a registration.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(backAsFirst, "first@example.com")).StatusCode);

        using var mine = JsonDocument.Parse(await backAsFirst.GetStringAsync("/api/bids"));
        var restoredBid = mine.RootElement.GetProperty(id);
        Assert.Equal(firstAmount, restoredBid.GetProperty("amount").GetInt32());
        Assert.True(restoredBid.GetProperty("outbid").GetBoolean(), "the second account still holds it");
        Assert.Equal(secondAmount, restoredBid.GetProperty("highest_amount").GetInt32());
    }

    [Fact]
    public async Task The_history_endpoint_lists_mine_and_names_the_vehicle()
    {
        await using var api = Api();
        var client = api.CreateClient();
        await Register(client, "first@example.com");
        long anchor = Anchor();
        var (id, amount) = await ALiveVehicle(client, anchor);
        await Bid(client, id, amount, anchor);

        using var history = JsonDocument.Parse(await client.GetStringAsync("/api/bids/history"));

        Assert.Equal(1, history.RootElement.GetProperty("count").GetInt32());
        var entry = history.RootElement.GetProperty("bids")[0];
        Assert.Equal(id, entry.GetProperty("vehicle_id").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(entry.GetProperty("title").GetString()),
            "a history nobody can read is a list of identifiers");
        Assert.Equal(amount, entry.GetProperty("bid").GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task Wrong_credentials_say_the_same_thing_as_no_account()
    {
        await using var api = Api();
        var client = api.CreateClient();
        await Register(client, "first@example.com");

        var stranger = api.CreateClient();
        var noSuchAccount = await stranger.PostAsJsonAsync(
            "/api/auth/login", new { email = "nobody@example.com", password = "correct horse" });
        var wrongPassword = await stranger.PostAsJsonAsync(
            "/api/auth/login", new { email = "first@example.com", password = "not the password" });

        Assert.Equal(HttpStatusCode.Unauthorized, noSuchAccount.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        // Two different messages would be an endpoint that tells a stranger
        // which email addresses have accounts here. Everything the caller is
        // told is compared, minus the trace id, which is a fresh request id on
        // every response by design (ADR: Error handling) and says nothing
        // about whether the account exists.
        Assert.Equal(await Told(noSuchAccount), await Told(wrongPassword));
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        await using var api = Api();
        var client = api.CreateClient();
        await Register(client, "first@example.com");
        Assert.Contains("first@example.com", await client.GetStringAsync("/api/auth/me"), StringComparison.Ordinal);

        await client.PostAsync("/api/auth/logout", content: null);

        string me = await client.GetStringAsync("/api/auth/me");
        Assert.Contains("\"signed_in\":false", me, StringComparison.Ordinal);
        long anchor = Anchor();
        var (id, amount) = await ALiveVehicle(client, anchor);
        var refused = await client.PostAsJsonAsync(
            $"/api/vehicles/{id}/bids", new { amount, anchor_ms = anchor });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_short_password_is_refused_with_something_a_person_can_act_on()
    {
        await using var api = Api();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { email = "first@example.com", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(
            "Eight characters",
            problem.RootElement.GetProperty("detail").GetString()!,
            StringComparison.Ordinal);
    }
    // #endregion auth-tests

    public void Dispose()
    {
        foreach (string leftover in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A test's scratch file that outlives the test is litter, not a failure.
            }
        }
        GC.SuppressFinalize(this);
    }
}
