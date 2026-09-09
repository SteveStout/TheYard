using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace TheYard.Api;

// #region proof
/// <summary>
/// The performance proof (ADR: Same performance, proven): the same requests a
/// visitor makes, sent by this container to itself, once per store per round,
/// in paired rounds that alternate which store goes first. Identical process,
/// identical request, identical request ring; only the store differs, and the
/// round trip to each store is measured on its own so the difference the
/// stores make can be told from the difference their distance makes.
///
/// <para>It runs in the background and keeps its last result, because eight
/// rounds of eight requests on two stores is half a minute, and an Admin tab
/// that hung for half a minute would be measuring the wrong thing. One run
/// at a time: a second request to start while one is running is told so.</para>
/// </summary>
public sealed class ProofRunner(
    Backends backends,
    ProofClients clients,
    SqlRingBuffer sqlLog,
    StoreRingBuffer storeLog)
{
    public const int DefaultRounds = 8;
    public const int MaxRounds = 20;

    /// <summary>The paths a round takes, in the order a visitor takes them, with the label the card shows.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Paths =
    [
        ("sign_in", "Sign in"),
        ("listing", "Listing page"),
        ("vehicle", "Vehicle page"),
        ("facets", "Filter values"),
        ("bid", "Bid write"),
        ("raise", "Bid raise"),
        ("reset", "Reset"),
    ];

    /// <summary>
    /// How long after one run the next may start. Starting a run is a write
    /// and needs a signed-in visitor (ADR: The one write a stranger can make,
    /// addendum), and the accounts a run bids with are made once per process
    /// and reused, so a loop of starts costs the site nothing but sixteen bids
    /// a minute on vehicles the proof picks; the minute is what keeps the card
    /// from being asked to prove the same thing twice at once.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProofResult? _last;
    private volatile bool _running;

    // #region proof-accounts
    /// <summary>
    /// The proof's own accounts, one per store, made on the first run this
    /// process performs and kept for the rest of its life: the address, the
    /// password it was registered with, and the registration's own sample, so
    /// later runs still show what registering cost without registering again.
    /// The first cut registered two fresh accounts per run, which made an
    /// anonymous loop of one start a minute exactly the hour's allowance of
    /// registrations (ADR: The one write a stranger can make, addendum). The
    /// password is random per process and lives nowhere but here.
    /// </summary>
    private readonly Dictionary<string, ProofAccount> _accounts = new(StringComparer.Ordinal);

    private readonly string _password = "proof-" + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    private sealed record ProofAccount(string Email, Sample Registration);
    // #endregion proof-accounts

    public bool Running => _running;

    public ProofResult? Last => _last;

    /// <summary>What the page reads: whether a run is on, and the last result if there is one.</summary>
    public object Status => new
    {
        status = _running ? "running" : _last is null ? "idle" : _last.Status,
        result = _last,
    };

    /// <summary>Start a run in the background; false when one is already running or the last one finished less than a minute ago.</summary>
    public bool TryStart(int rounds)
    {
        if (_last?.FinishedAt is { } finished && DateTimeOffset.UtcNow - finished < Cooldown)
        {
            return false;
        }
        if (!_gate.Wait(0))
        {
            return false;
        }

        _running = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _last = await RunAsync(rounds);
            }
            catch (Exception ex)
            {
                // The type, not the message: this result is served on a public
                // page, and an exception message can carry a host name.
                _last = ProofResult.Failed($"the proof stopped on {ex.GetType().Name}", backends);
            }
            finally
            {
                _running = false;
                _gate.Release();
            }
        });
        return true;
    }

    // #region rounds
    public async Task<ProofResult> RunAsync(int rounds)
    {
        rounds = Math.Clamp(rounds, 1, MaxRounds);
        if (backends.All.Count < 2)
        {
            return ProofResult.Failed("this container runs one store, and a proof needs two", backends);
        }

        var started = DateTimeOffset.UtcNow;
        var stores = backends.All.Select(backend => new StoreRun(backend)).ToArray();
        using var client = clients.Create();

        // One account per store for the life of the process: registered and
        // timed on the first run, signed into on every run after that. A run
        // that finds its account gone (the store was reset under it) registers
        // a fresh one, which is the one case that spends a registration.
        foreach (var store in stores)
        {
            if (_accounts.TryGetValue(store.Backend.Key, out var account) && await SignsInAsync(client, store, account.Email))
            {
                store.Email = account.Email;
                store.Samples.Add(account.Registration);
                continue;
            }

            string email = $"proof-{Guid.NewGuid():N}@example.com";
            using var registered = await Timed(client, store, "register", HttpMethod.Post, "/api/auth/register",
                new { email, password = _password });
            if (!registered.IsSuccessStatusCode)
            {
                return ProofResult.Failed($"registration on {store.Backend.Name} answered {(int)registered.StatusCode}", backends);
            }
            store.Cookie = SessionCookie(registered);
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
                await RoundAsync(client, store);
            }
        }

        // The round trip to each store on its own: a statement that does no
        // work on the relational side, a point read of one document on the
        // document side. Five each, the median kept.
        foreach (var store in stores)
        {
            store.HopMs = await HopAsync(store.Backend);
        }

        return ProofResult.Of(started, rounds, stores);
    }

    private async Task RoundAsync(HttpClient client, StoreRun store)
    {
        using var signedIn = await Timed(client, store, "sign_in", HttpMethod.Post, "/api/auth/login",
            new { email = store.Email, password = _password });
        if (signedIn.IsSuccessStatusCode)
        {
            store.Cookie = SessionCookie(signedIn) ?? store.Cookie;
        }

        using var listing = await Timed(client, store, "listing", HttpMethod.Get, "/api/vehicles?status=live&sort=most-bids&limit=25");
        string? vehicleId = await ChooseVehicleAsync(listing);
        if (vehicleId is null)
        {
            return;
        }

        using var vehicle = await Timed(client, store, "vehicle", HttpMethod.Get, $"/api/vehicles/{vehicleId}");
        int? minimum = await MinimumAsync(vehicle);
        using var facets = await Timed(client, store, "facets", HttpMethod.Get, "/api/facets");

        if (minimum is { } first)
        {
            using var bid = await Timed(client, store, "bid", HttpMethod.Post, $"/api/vehicles/{vehicleId}/bids",
                new { amount = first, anchor_ms = Anchor() });
            int? next = await NextAsync(bid);
            if (next is { } second)
            {
                using var raise = await Timed(client, store, "raise", HttpMethod.Post, $"/api/vehicles/{vehicleId}/bids",
                    new { amount = second, anchor_ms = Anchor() });
            }
        }

        using var reset = await Timed(client, store, "reset", HttpMethod.Delete, "/api/bids");
    }
    // #endregion rounds

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

    private static long Anchor() =>
        new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

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

    /// <summary>One store's side of the run: its account, its cookie, and every sample taken on it.</summary>
    public sealed class StoreRun(Backend backend)
    {
        public Backend Backend { get; } = backend;
        public string? Cookie { get; set; }
        public string Email { get; set; } = "";
        public long? HopMs { get; set; }
        public List<Sample> Samples { get; } = [];
    }

    public sealed record Sample(string Path, long Ms, int Status, int Operations, double RequestUnits);
}
// #endregion proof

/// <summary>
/// Where the proof's requests go: this container's own loopback address in
/// production, and whatever a test hands in. A factory rather than a client,
/// because a run wants a fresh client and a test wants to choose it.
/// </summary>
public sealed class ProofClients(Func<HttpClient> create)
{
    public HttpClient Create() => create();
}

// #region proof-result
/// <summary>One path on one store: the samples, the medians, and what the store did per request.</summary>
public sealed record ProofCell(
    string Store,
    int Samples,
    long P50Ms,
    long P95Ms,
    double OperationsPerRequest,
    double? RequestUnitsPerRequest,
    int Failures);

/// <summary>
/// One row of the proof: a path, both stores, and the median of the paired
/// differences, the second store's time minus the first's per round, so a
/// negative number means the second store was faster. The second difference
/// is the same with one round trip per operation taken off each side.
/// </summary>
public sealed record ProofRow(
    string Path,
    string Label,
    IReadOnlyList<ProofCell> Cells,
    long? MedianDifferenceMs,
    long? DifferenceWithoutHopsMs,
    string Verdict);

public sealed record ProofStore(string Key, string Name, long? HopMs);

public sealed record ProofResult(
    string Status,
    string? Reason,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Rounds,
    IReadOnlyList<ProofStore> Stores,
    IReadOnlyList<ProofRow> Rows,
    string Sentence)
{
    public static ProofResult Failed(string reason, Backends backends) => new(
        "failed",
        reason,
        null,
        DateTimeOffset.UtcNow,
        0,
        backends.All.Select(backend => new ProofStore(backend.Key, backend.Name, null)).ToArray(),
        [],
        reason);

    // #region verdict
    /// <summary>
    /// How much the two stores are allowed to differ and still be called the
    /// same: fifteen milliseconds or fifteen per cent of the slower one,
    /// whichever is more. Fifteen milliseconds is about the spread between
    /// two rounds of the same request on the same store.
    /// </summary>
    public static bool Same(long difference, long slower) =>
        Math.Abs(difference) <= Math.Max(15, slower * 0.15);

    public static ProofResult Of(DateTimeOffset started, int rounds, IReadOnlyList<ProofRunner.StoreRun> stores)
    {
        var first = stores[0];
        var second = stores[1];
        var rows = new List<ProofRow>();
        foreach (var (path, label) in ProofRunner.Paths.Prepend(("register", "Register")))
        {
            var cells = stores.Select(store => Cell(store, path)).ToArray();
            var a = first.Samples.Where(s => s.Path == path && s.Status < 400).Select(s => s.Ms).ToArray();
            var b = second.Samples.Where(s => s.Path == path && s.Status < 400).Select(s => s.Ms).ToArray();
            int pairs = Math.Min(a.Length, b.Length);
            long? difference = pairs == 0 ? null : Percentiles.Of(Enumerable.Range(0, pairs).Select(i => b[i] - a[i]).ToArray(), 50);
            long? withoutHops = difference is null
                ? null
                : difference - (long)Math.Round(
                    (cells[1].OperationsPerRequest * (second.HopMs ?? 0)) - (cells[0].OperationsPerRequest * (first.HopMs ?? 0)));
            rows.Add(new ProofRow(path, label, cells, difference, withoutHops, Verdict(difference, withoutHops, cells, first.Backend.Name, second.Backend.Name)));
        }

        return new ProofResult(
            "done",
            null,
            started,
            DateTimeOffset.UtcNow,
            rounds,
            stores.Select(store => new ProofStore(store.Backend.Key, store.Backend.Name, store.HopMs)).ToArray(),
            rows,
            SentenceFor(rows, stores));
    }

    private static ProofCell Cell(ProofRunner.StoreRun store, string path)
    {
        var all = store.Samples.Where(s => s.Path == path).ToArray();
        var ok = all.Where(s => s.Status < 400).ToArray();
        long[] ms = ok.Select(s => s.Ms).ToArray();
        return new ProofCell(
            store.Backend.Name,
            ok.Length,
            Percentiles.Of(ms, 50),
            Percentiles.Of(ms, 95),
            ok.Length == 0 ? 0 : Math.Round(ok.Average(s => s.Operations), 1),
            store.Backend.Cosmos is null || ok.Length == 0 ? null : Math.Round(ok.Average(s => s.RequestUnits), 2),
            all.Length - ok.Length);
    }

    private static string Verdict(long? difference, long? withoutHops, ProofCell[] cells, string firstName, string secondName)
    {
        if (difference is null)
        {
            return "not measured";
        }
        long slower = Math.Max(cells[0].P50Ms, cells[1].P50Ms);
        if (Same(difference.Value, slower))
        {
            return "the same";
        }
        string leader = difference < 0 ? secondName : firstName;
        string sentence = $"{leader} leads by {Math.Abs(difference.Value)} ms";
        if (withoutHops is { } adjusted && Same(adjusted, slower))
        {
            sentence += ", all of it the round trip to the store";
        }
        return sentence;
    }

    private static string SentenceFor(IReadOnlyList<ProofRow> rows, IReadOnlyList<ProofRunner.StoreRun> stores)
    {
        int same = rows.Count(row => row.Verdict == "the same");
        int measured = rows.Count(row => row.MedianDifferenceMs is not null);
        int hops = rows.Count(row => row.Verdict.EndsWith("the round trip to the store", StringComparison.Ordinal));
        string first = stores[0].Backend.Name;
        string second = stores[1].Backend.Name;
        string trips = string.Join(" and ", stores.Select(store => $"{store.HopMs?.ToString() ?? "?"} ms to {store.Backend.Name}"));
        if (measured == 0)
        {
            return "Nothing could be measured; the rows say why.";
        }
        if (same == measured)
        {
            return $"On every path measured, {first} and {second} answer in the same time, within the tolerance the record states. One round trip to the store is {trips}.";
        }
        if (same + hops == measured)
        {
            return $"On {same} of {measured} paths the two stores answer in the same time. On the other {hops} the difference is the round trip to the store, {trips}, and taking one round trip per operation off each side leaves them the same.";
        }
        return $"On {same} of {measured} paths the two stores answer in the same time; {hops} more differ by exactly the round trip to the store ({trips}); the rest differ by more than that, and the rows say by how much.";
    }
    // #endregion verdict
}
// #endregion proof-result
