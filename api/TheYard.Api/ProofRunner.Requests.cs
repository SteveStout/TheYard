// The proof's single requests: one timed request to this container with the clock around it,
// the untimed sign-in check, and the readers that pull a cookie, a vehicle or a bid out of
// an answer, plus the bare round trip to each store. Kept apart from the rounds so the
// rounds read as the visitor's path and this reads as how each step is measured.
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace TheYard.Api;

/// <summary>The timed requests of ProofRunner; the type and what it is for are described in Proof.cs.</summary>
public sealed partial class ProofRunner
{
    // #region timed
    /// <summary>
    /// One request to this container, on one store, with the clock around the
    /// whole exchange and the two rings read for what the request caused: how
    /// many statements or operations, and what the document store charged.
    /// </summary>
    private async Task<HttpResponseMessage> Timed(HttpClient client, StoreRun store, string path, HttpMethod method, string url, object? body = null)
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

        var at = DateTimeOffset.UtcNow;
        long start = Stopwatch.GetTimestamp();
        var response = await client.SendAsync(request);
        // The body is part of what a visitor waits for.
        await response.Content.LoadIntoBufferAsync();
        long elapsedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        int statements = store.Backend.Contexts is null ? 0 : sqlLog.Snapshot().Count(statement => statement.At >= at);
        var operations = store.Backend.Cosmos is null
            ? []
            : storeLog.Snapshot().Where(operation => operation.At >= at).ToArray();
        store.Samples.Add(new Sample(path, elapsedMs, (int)response.StatusCode, statements + operations.Length, operations.Sum(operation => operation.RequestCharge)));
        return response;
    }

    /// <summary>
    /// Whether the proof's remembered account still signs in on this store.
    /// Not timed and not sampled: it is a check that the account survived,
    /// and the round's own sign-in is the measurement.
    /// </summary>
    private async Task<bool> SignsInAsync(HttpClient client, StoreRun store, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add(Backends.HeaderName, store.Backend.Key);
        request.Content = JsonContent.Create(new { email, password = _password });
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }
        store.Cookie = SessionCookie(response);
        return true;
    }

    /// <summary>The session cookie a sign-in or registration set, as name=value ready to send back, or null when it set none.</summary>
    private static string? SessionCookie(HttpResponseMessage response)
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

    /// <summary>A live vehicle with ten minutes left and room under buy-now for a bid and a raise.</summary>
    private static async Task<string?> ChooseVehicleAsync(HttpResponseMessage listing)
    {
        if (!listing.IsSuccessStatusCode)
        {
            return null;
        }
        var body = await listing.Content.ReadFromJsonAsync<JsonElement>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
    private static async Task<int?> MinimumAsync(HttpResponseMessage vehicle)
    {
        if (!vehicle.IsSuccessStatusCode)
        {
            return null;
        }
        var body = await vehicle.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("min_next_bid", out var minimum) ? minimum.GetInt32() : null;
    }

    /// <summary>The next acceptable bid after an accepted one, from the vehicle the bid answered with.</summary>
    private static async Task<int?> NextAsync(HttpResponseMessage bid)
    {
        if (!bid.IsSuccessStatusCode)
        {
            return null;
        }
        var body = await bid.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("kind", out var kind) && kind.GetString() != "accepted")
        {
            return null;
        }
        return body.TryGetProperty("vehicle", out var vehicle) && vehicle.TryGetProperty("min_next_bid", out var minimum)
            ? minimum.GetInt32()
            : null;
    }

    /// <summary>The median of five round trips to the store, doing as little work as a round trip can.</summary>
    private static async Task<long?> HopAsync(Backend backend)
    {
        var samples = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            long start = Stopwatch.GetTimestamp();
            if (backend.Cosmos is { } cosmos)
            {
                // Two point reads, so half the time is one.
                await cosmos.ProbeAsync();
                samples.Add((long)(Stopwatch.GetElapsedTime(start).TotalMilliseconds / 2));
            }
            else if (backend.Contexts is { } contexts)
            {
                using var db = contexts.CreateDbContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                samples.Add((long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            else
            {
                return null;
            }
        }
        return Percentiles.Of(samples, 50);
    }
    // #endregion timed
}
