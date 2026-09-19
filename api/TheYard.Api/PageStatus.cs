using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace TheYard.Api;

// #region self-address
/// <summary>
/// The address this container can dial itself on, read from the server rather
/// than from a variable somebody set earlier.
///
/// <para>Earlier is the word that matters. The proof reads the bound address
/// in a callback on <c>ApplicationStarted</c>, and the first cut of this sweep
/// read the variable that callback fills. It was always null: a
/// <c>CancellationToken</c> runs its callbacks in the reverse of the order
/// they were registered, so the sweep, registered last, ran first, before the
/// address had been read. It was the browser suite that found it, and the
/// fix is not to register in the other order but to stop depending on the
/// order at all. The server knows what it is listening on, and this asks
/// it.</para>
///
/// <para>A bound address is not always one a client can dial: Kestrel reports
/// the wildcard it bound, and `localhost` inside a container resolves to a
/// stack that may not be the one it bound. Both become the loopback. HTTPS
/// addresses are skipped: this container serves plain HTTP behind the edge,
/// and a self-signed hop would be a certificate question rather than a page
/// check.</para>
/// </summary>
public static class SelfAddress
{
    public static string? Of(IServiceProvider services)
    {
        var addresses = services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
        foreach (string address in addresses ?? (IEnumerable<string>)[])
        {
            if (Dialable(address) is { } dialable)
            {
                return dialable;
            }
        }
        return null;
    }

    /// <summary>One bound address as something a client can dial, or null when it is not one this can use.</summary>
    public static string? Dialable(string address)
    {
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string normalized = address
            .Replace("://+:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://*:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://[::]:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://0.0.0.0:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://localhost:", "://127.0.0.1:", StringComparison.Ordinal);

        return Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            ? parsed.GetLeftPart(UriPartial.Authority)
            : null;
    }
}
// #endregion self-address

// #region served-addresses
/// <summary>
/// Every address this container answers a page, a document or a drawing at,
/// derived from the catalogue rather than written out beside it (ADR: Every
/// page, checked at every roll). A list would be a second place to remember,
/// and the first document somebody adds without remembering is the one that
/// goes out broken, which is exactly the shape the README's two raw-markdown
/// links had: every link answered, and nothing asked what it answered with.
/// The fixed rows below are the addresses that are not documents: the app
/// itself, the API's own front pages, and the three files the build copies to
/// the root of the domain.
///
/// <para>The four that come out of the frontend build are checked only when
/// the frontend is in this container. In the image it always is; on a
/// developer's machine and under the test host the API runs on its own with
/// the dev server in front of it, and calling four addresses this container
/// was never given down would be a false reading rather than a strict one.</para>
/// </summary>
public static class ServedAddresses
{
    /// <summary>The addresses the frontend build puts in this container's web root.</summary>
    public static readonly IReadOnlyList<ServedAddress> FromTheBuild =
    [
        new("/", "The app", "page"),
        new("/robots.txt", "robots.txt", "file"),
        new("/sitemap.xml", "sitemap.xml", "file"),
        new("/og.png", "The preview card", "file"),
    ];

    public static IReadOnlyList<ServedAddress> All(bool frontendServed)
    {
        var addresses = new List<ServedAddress>
        {
            new("/api/reference", "API reference", "page"),
            new("/api/version", "The build this container was made from", "api"),
            new("/api/health", "Health, both stores", "api"),
            new("/api/vehicles?limit=1", "The listing", "api"),
            new("/api/facets", "The filter values", "api"),
            new("/api/stores", "The stores this container runs", "api"),
            new("/api/docs/resume", "Steven's resume (PDF)", "file"),
            new("/api/docs/bicep", "Infrastructure (Bicep)", "document"),
        };

        if (frontendServed)
        {
            addresses.InsertRange(0, FromTheBuild);
        }

        addresses.AddRange(DocsCatalog.Files
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ServedAddress($"/api/docs/{entry.Key}", entry.Value, "document")));

        addresses.AddRange(DocsCatalog.Diagrams
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ServedAddress($"/api/docs/diagrams/{entry.Key}", entry.Value.Title, "drawing")));

        return addresses;
    }
}

/// <summary>One address the sweep checks: where it is, what a reader would call it, and which kind of thing it is.</summary>
public sealed record ServedAddress(string Address, string What, string Kind);
// #endregion served-addresses

// #region page-status
/// <summary>
/// The sweep: this container asks itself for every address above and records
/// what came back. It runs once at startup, which makes every roll carry a
/// check of the thing that was just rolled, and again whenever the Admin tab
/// asks for one.
///
/// <para>It dials its own loopback address, the way the proof does, so what it
/// reports is what this container serves rather than what the edge has cached.
/// The edge is checked from outside after every ship and that is a different
/// question; the card says which one this is.</para>
///
/// <para>Every request carries <c>X-Yard-Page-Check</c>, and the request hook
/// drops anything carrying it: ninety self-requests a roll would otherwise
/// push a morning of real traffic out of a five-hundred slot ring and add a
/// visitor to the activity card who is this container.</para>
/// </summary>
public sealed class PageStatusRunner(Func<HttpClient?> createClient, Func<bool> frontendServed, string version, string commit)
{
    /// <summary>The header the request hook drops on. A sweep is not traffic.</summary>
    public const string CheckHeader = "X-Yard-Page-Check";

    /// <summary>Four at a time. Enough to finish a roll's sweep in a second or two, few enough that the container is still a web server while it runs.</summary>
    public const int AtOnce = 4;

    /// <summary>How long after one sweep the next may start, so a card left open cannot ask for one a second.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private PageStatusReport? _last;
    private volatile bool _running;

    public bool Running => _running;

    public PageStatusReport? Last => _last;

    /// <summary>What the card reads: whether a sweep is on, and the last one if there has been one.</summary>
    public object Status => new
    {
        status = _running ? "running" : _last is null ? "not run" : "done",
        report = _last,
    };

    /// <summary>
    /// Start a sweep in the background. False when one is running, when the
    /// last one finished inside the cooldown, or when this process has no
    /// address of its own to dial, which is the case under the test host: the
    /// suite drives <see cref="RunAsync"/> with its own client instead.
    /// </summary>
    public bool TryStart(string trigger)
    {
        if (_last?.At is { } last && DateTimeOffset.UtcNow - last < Cooldown)
        {
            return false;
        }
        if (createClient() is null || !_gate.Wait(0))
        {
            return false;
        }

        _running = true;
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = createClient();
                if (client is not null)
                {
                    _last = await RunAsync(client, trigger, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // The type, never the message. This report is served on a
                // public page and a message can carry a host and a port.
                _last = PageStatusReport.Stopped(trigger, version, commit, ex.GetType().Name);
            }
            finally
            {
                _running = false;
                _gate.Release();
            }
        });
        return true;
    }

    /// <summary>Ask for every address once, four at a time, and report what each one answered.</summary>
    public async Task<PageStatusReport> RunAsync(HttpClient client, string trigger, CancellationToken cancellation)
    {
        var addresses = ServedAddresses.All(frontendServed());
        var entries = new PageStatusEntry[addresses.Count];
        var whole = Stopwatch.StartNew();
        using var atOnce = new SemaphoreSlim(AtOnce, AtOnce);

        await Task.WhenAll(addresses.Select(async (address, index) =>
        {
            await atOnce.WaitAsync(cancellation);
            try
            {
                entries[index] = await CheckAsync(client, address, cancellation);
            }
            finally
            {
                atOnce.Release();
            }
        }));

        return new PageStatusReport(
            DateTimeOffset.UtcNow,
            trigger,
            version,
            commit,
            whole.ElapsedMilliseconds,
            entries.Length,
            entries.Count(entry => entry.Ok),
            null,
            entries);
    }

    private static async Task<PageStatusEntry> CheckAsync(HttpClient client, ServedAddress address, CancellationToken cancellation)
    {
        var timer = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, address.Address);
        request.Headers.TryAddWithoutValidation(CheckHeader, "1");
        try
        {
            using var response = await client.SendAsync(request, cancellation);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellation);
            timer.Stop();
            return new PageStatusEntry(
                address.Address,
                address.What,
                address.Kind,
                (int)response.StatusCode,
                timer.ElapsedMilliseconds,
                body.LongLength,
                response.Content.Headers.ContentType?.MediaType,
                null,
                // An address that answers 200 with nothing in it is down as
                // far as a reader is concerned, so the emptiness counts.
                response.IsSuccessStatusCode && body.LongLength > 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            timer.Stop();
            return new PageStatusEntry(address.Address, address.What, address.Kind, 0, timer.ElapsedMilliseconds, 0, null, ex.GetType().Name, false);
        }
    }
}

/// <summary>One address as the sweep found it. <c>Reason</c> is a type name and never a message, for the reason on the runner.</summary>
public sealed record PageStatusEntry(
    string Address,
    string What,
    string Kind,
    int Status,
    long Ms,
    long Bytes,
    string? ContentType,
    string? Reason,
    bool Ok);

/// <summary>One sweep: when, what started it, the build it checked, and every address.</summary>
public sealed record PageStatusReport(
    DateTimeOffset At,
    string Trigger,
    string Version,
    string Commit,
    long Ms,
    int Checked,
    int Up,
    string? Failed,
    IReadOnlyList<PageStatusEntry> Entries)
{
    /// <summary>A sweep that stopped on something. Named apart from the property it fills so the record can carry both.</summary>
    public static PageStatusReport Stopped(string trigger, string version, string commit, string reason) =>
        new(DateTimeOffset.UtcNow, trigger, version, commit, 0, 0, 0, reason, []);
}
// #endregion page-status
