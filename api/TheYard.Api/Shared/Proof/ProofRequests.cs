// The proof's single requests: one timed request to this container with the clock around it, and
// the readers that pull a cookie, a vehicle or a bid out of an answer, plus the bare round trip to
// each store. Its own type, apart from the runner, so the rounds read as the visitor's path and this
// reads as how each step is measured; it changes when a measurement does, never when the path does.
using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Api.Shared.Proof;

/// <summary>How the proof measures one step: the request with the clock and the two rings around it, and the readers of each answer.</summary>
/// <param name="sqlLog">The relational store's statement ring, read for what a request caused.</param>
/// <param name="storeLog">The document store's operation ring, read for what a request caused and charged.</param>
/// <param name="clock">The clock the measurement is stamped by, handed in so a test can move it.</param>
public sealed class ProofRequests(SqlRingBuffer sqlLog, StoreRingBuffer storeLog, TimeProvider clock)
{
    // #region timed
    /// <summary>
    /// One request to this container, on one store, with the clock around the
    /// whole exchange and the two rings read for what the request caused: how
    /// many statements or operations, and what the document store charged.
    /// </summary>
    /// <param name="client">The loopback client the request goes out on.</param>
    /// <param name="store">The store's side of the run, which the sample is added to.</param>
    /// <param name="path">The key of the path being measured, such as sign_in or listing.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The address on this container.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run: a container being replaced stops measuring itself.</param>
    /// <param name="body">What to send, or null for a request with none.</param>
    public async Task<HttpResponseMessage> Timed(Outbound client, ProofRunner.StoreRun store, string path, HttpMethod method, string url, CancellationToken cancellation, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(Backends.HeaderName, store.Backend.Key);
        if (store.Cookie is not null)
        {
            request.Headers.Add("Cookie", store.Cookie);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var at = clock.GetUtcNow();
        long start = Stopwatch.GetTimestamp();
        var response = await client.SendAsync(request, cancellation);
        // The body is part of what a visitor waits for.
        await response.Content.LoadIntoBufferAsync(cancellation);
        long elapsedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        int statements = store.Backend.LogsStatements ? sqlLog.Snapshot().Count(statement => statement.At >= at) : 0;
        var operations = store.Backend.LogsOperations
            ? storeLog.Snapshot().Where(operation => operation.At >= at).ToArray()
            : [];
        store.Samples.Add(new ProofRunner.Sample(path, elapsedMs, (int)response.StatusCode, statements + operations.Length, operations.Sum(operation => operation.RequestCharge)));
        return response;
    }

    /// <summary>The session cookie a sign-in or registration set, as name=value ready to send back, or null when it set none.</summary>
    public static string? SessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }
        foreach (string cookie in cookies)
        {
            if (cookie.StartsWith(TokenIssuer.CookieName + "=", StringComparison.Ordinal))
            {
                return cookie.Split(';', 2)[0];
            }
        }
        return null;
    }

    /// <summary>
    /// A live vehicle with ten minutes left and room under buy-now for a bid and a raise. Reads the
    /// ten minutes off the handed clock, because what counts as live is a question about now.
    /// </summary>
    /// <param name="listing">The listing page's answer.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run.</param>
    public async Task<string?> ChooseVehicleAsync(HttpResponseMessage listing, CancellationToken cancellation)
    {
        if (!listing.IsSuccessStatusCode)
        {
            return null;
        }
        var body = await listing.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var vehicle in body.GetProperty("vehicles").EnumerateArray())
        {
            long endsAt = vehicle.GetProperty("auction_ends_at").GetInt64();
            int price = vehicle.TryGetProperty("current_bid", out var current) && current.ValueKind == JsonValueKind.Number
                ? current.GetInt32()
                : vehicle.GetProperty("starting_bid").GetInt32();
            bool underBuyNow = !vehicle.TryGetProperty("buy_now_price", out var buyNow)
                || buyNow.ValueKind != JsonValueKind.Number
                || price + 2_000 < buyNow.GetInt32();
            if (endsAt - now > 10 * 60_000 && underBuyNow)
            {
                return vehicle.GetProperty("id").GetString();
            }
        }
        return null;
    }

    /// <summary>The lowest bid the vehicle page will accept, read from its min_next_bid, or null when the page did not answer.</summary>
    /// <param name="vehicle">The vehicle page's answer.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run.</param>
    public static async Task<int?> MinimumAsync(HttpResponseMessage vehicle, CancellationToken cancellation)
    {
        if (!vehicle.IsSuccessStatusCode)
        {
            return null;
        }
        var body = await vehicle.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        return body.TryGetProperty("min_next_bid", out var minimum) ? minimum.GetInt32() : null;
    }

    /// <summary>The next acceptable bid after an accepted one, from the vehicle the bid answered with.</summary>
    /// <param name="bid">The bid write's answer.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run.</param>
    public static async Task<int?> NextAsync(HttpResponseMessage bid, CancellationToken cancellation)
    {
        if (!bid.IsSuccessStatusCode)
        {
            return null;
        }
        var body = await bid.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        if (body.TryGetProperty("kind", out var kind) && kind.GetString() != "accepted")
        {
            return null;
        }
        return body.TryGetProperty("vehicle", out var vehicle) && vehicle.TryGetProperty("min_next_bid", out var minimum)
            ? minimum.GetInt32()
            : null;
    }

    /// <summary>The median of five round trips to the store, doing as little work as a round trip can.</summary>
    /// <param name="backend">The store to reach, asked through its own diagnostics.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run; five round trips is five chances to stop.</param>
    public static async Task<long?> HopAsync(Backend backend, CancellationToken cancellation)
    {
        var samples = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            // Asked of the store through its diagnostics, which each adapter answers its own way.
            if (await backend.Diagnostics.RoundTripMillisecondsAsync(cancellation) is not { } milliseconds)
            {
                return null;
            }
            samples.Add((long)milliseconds);
        }
        return Percentiles.Of(samples, 50);
    }
    // #endregion timed
}
