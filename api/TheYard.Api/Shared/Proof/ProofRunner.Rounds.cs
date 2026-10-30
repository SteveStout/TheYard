// One proof run from start to finish: the accounts it signs in with, the paired rounds that
// alternate which store goes first, and the round trip to each store at the end. It is its
// own file because this is the measurement's design, the part a reader checks for fairness.
// The runner itself starts in Proof.cs.
namespace TheYard.Api.Shared.Proof;

/// <summary>The paired rounds of ProofRunner; the type and what it is for are described in Proof.cs.</summary>
public sealed partial class ProofRunner
{
    // #region rounds
    /// <summary>
    /// One whole run: make sure each store has a signed-in account, take the
    /// paired rounds, measure the round trip to each store, and sum it up.
    /// Clamps the rounds to between one and <see cref="MaxRounds"/>.
    /// </summary>
    /// <param name="rounds">How many paired rounds to take, clamped to between one and <see cref="MaxRounds"/>.</param>
    /// <param name="cancellation">The host's stopping token, which the starter lent this run. Not
    /// the token of the request that asked: that request is answered the moment the run starts and
    /// the run goes on for half a minute after it.</param>
    public async Task<ProofResult> RunAsync(int rounds, CancellationToken cancellation)
    {
        rounds = Math.Clamp(rounds, 1, MaxRounds);
        if (backends.All.Count < 2)
        {
            return ProofResult.Failed("this container runs one store, and a proof needs two", backends, clock.GetUtcNow());
        }

        var started = clock.GetUtcNow();
        var stores = backends.All.Select(backend => new StoreRun(backend)).ToArray();
        using var client = clients.Create();

        // One account per store for the life of the process: registered and
        // timed on the first run, signed into on every run after that. A run
        // that finds its account gone (the store was reset under it) registers
        // a fresh one, which is the one case that spends a registration.
        foreach (var store in stores)
        {
            if (_accounts.TryGetValue(store.Backend.Key, out var account) && await SignsInAsync(client, store, account.Email, cancellation))
            {
                store.Email = account.Email;
                store.Samples.Add(account.Registration);
                continue;
            }

            string email = $"proof-{Guid.NewGuid():N}@example.com";
            using var registered = await requests.Timed(client, store, "register", HttpMethod.Post, "/api/auth/register",
                cancellation, new { email, password = _password });
            if (!registered.IsSuccessStatusCode)
            {
                return ProofResult.Failed($"registration on {store.Backend.Name} answered {(int)registered.StatusCode}", backends, clock.GetUtcNow());
            }
            store.Cookie = ProofRequests.SessionCookie(registered);
            store.Email = email;
            _accounts[store.Backend.Key] = new ProofAccount(email, store.Samples.Last(sample => sample.Path == "register"));
        }

        for (int round = 0; round < rounds; round++)
        {
            // Alternate which store goes first, so whichever effect order has
            // (a warm cache, a busy second) lands on both sides equally.
            var order = round % 2 == 0 ? stores : stores.Reverse().ToArray();
            foreach (var store in order)
            {
                await RoundAsync(client, store, cancellation);
            }
        }

        // The round trip to each store on its own: a statement that does no
        // work on the relational side, a point read of one document on the
        // document side. Five each, the median kept.
        foreach (var store in stores)
        {
            store.HopMs = await ProofRequests.HopAsync(store.Backend, cancellation);
        }

        return ProofResult.Of(started, clock.GetUtcNow(), rounds, stores);
    }

    /// <summary>
    /// One round on one store, the way a visitor goes: sign in, open the
    /// listing, open a vehicle, read the filter values, bid, raise the bid,
    /// and reset. A round with no vehicle worth bidding on stops after the
    /// listing rather than bidding on one that would end or sell mid-run.
    /// </summary>
    /// <param name="client">The loopback client every request in the round goes out on.</param>
    /// <param name="store">The store's side of the run.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run.</param>
    private async Task RoundAsync(Outbound client, StoreRun store, CancellationToken cancellation)
    {
        using var signedIn = await requests.Timed(client, store, "sign_in", HttpMethod.Post, "/api/auth/login",
            cancellation, new { email = store.Email, password = _password });
        if (signedIn.IsSuccessStatusCode)
        {
            store.Cookie = ProofRequests.SessionCookie(signedIn) ?? store.Cookie;
        }

        using var listing = await requests.Timed(client, store, "listing", HttpMethod.Get, "/api/vehicles?status=live&sort=most-bids&limit=25", cancellation);
        string? vehicleId = await requests.ChooseVehicleAsync(listing, cancellation);
        if (vehicleId is null)
        {
            return;
        }

        using var vehicle = await requests.Timed(client, store, "vehicle", HttpMethod.Get, $"/api/vehicles/{vehicleId}", cancellation);
        int? minimum = await ProofRequests.MinimumAsync(vehicle, cancellation);
        using var facets = await requests.Timed(client, store, "facets", HttpMethod.Get, "/api/facets", cancellation);

        if (minimum is { } first)
        {
            using var bid = await requests.Timed(client, store, "bid", HttpMethod.Post, $"/api/vehicles/{vehicleId}/bids",
                cancellation, new { amount = first });
            int? next = await ProofRequests.NextAsync(bid, cancellation);
            if (next is { } second)
            {
                using var raise = await requests.Timed(client, store, "raise", HttpMethod.Post, $"/api/vehicles/{vehicleId}/bids",
                    cancellation, new { amount = second });
            }
        }

        using var reset = await requests.Timed(client, store, "reset", HttpMethod.Delete, "/api/bids", cancellation);
    }
    /// <summary>
    /// Whether the proof's remembered account still signs in on this store.
    /// Not timed and not sampled: it is a check that the account survived,
    /// and the round's own sign-in is the measurement.
    /// </summary>
    /// <param name="client">The loopback client the check goes out on.</param>
    /// <param name="store">The store's side of the run.</param>
    /// <param name="email">The address the proof's account on this store was registered with.</param>
    /// <param name="cancellation">The host's stopping token, lent by the run.</param>
    private async Task<bool> SignsInAsync(Outbound client, StoreRun store, string email, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add(Backends.HeaderName, store.Backend.Key);
        request.Content = JsonContent.Create(new { email, password = _password });
        using var response = await client.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }
        store.Cookie = ProofRequests.SessionCookie(response);
        return true;
    }
    // #endregion rounds
}
