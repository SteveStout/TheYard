// The page sweep starts here: the runner that asks this container for every address it serves,
// and the report it keeps. Its two helpers live beside it:
//   ServedAddresses.cs - the list of addresses to check, built from the catalogue
//   SelfAddress.cs     - the address this container dials itself on
using System.Diagnostics;

namespace TheYard.Api;

// #region page-status
/// <summary>
/// The sweep: this container asks itself for every address in
/// <see cref="ServedAddresses"/> and records
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

    /// <summary>How long after a roll's sweep the settled process sweeps itself again, because the first sweep runs in the busiest second the process has.</summary>
    public static readonly TimeSpan SecondSweep = TimeSpan.FromMinutes(3);

    /// <summary>The one-at-a-time gate: a sweep holds it from start to finish.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The report of the last sweep that finished or stopped, or null before the first.</summary>
    private PageStatusReport? _last;

    /// <summary>Whether a sweep is on right now; read from request threads while the sweep's own thread writes it.</summary>
    private volatile bool _running;

    /// <summary>Whether a sweep is on right now.</summary>
    public bool Running => _running;

    /// <summary>The last sweep's report, or null when no sweep has finished yet.</summary>
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

        // #region second-look
        // An address that did not answer gets a second look, alone, once the
        // first pass is over. The roll's sweep runs in the busiest second a
        // process has, four addresses at once while both containers warm their
        // catalogues on the one core they share, and in that second a slow
        // address such as /api/health can take past the thirty seconds and be
        // reported down on a tile that keeps saying so until the next roll. An
        // address that answers on its own a moment later was never down.
        for (int index = 0; index < entries.Length; index++)
        {
            if (!entries[index].Ok)
            {
                var again = await CheckAsync(client, addresses[index], cancellation);
                entries[index] = again.Ok ? again with { Reason = "answered on a second look" } : again;
            }
        }
        // #endregion second-look

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

    /// <summary>
    /// Ask for one address, marked as a check so the request hook drops it,
    /// and record what it answered. A request that fails or times out is an
    /// entry that is down with the exception's type name, never a throw.
    /// </summary>
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
/// <param name="Address">The path that was requested.</param>
/// <param name="What">What a reader would call the address.</param>
/// <param name="Kind">Which kind of thing it is, such as page, api, document or drawing.</param>
/// <param name="Status">The HTTP status code; 0 when the request did not complete.</param>
/// <param name="Ms">How long the check took, in milliseconds.</param>
/// <param name="Bytes">How many bytes the body held.</param>
/// <param name="ContentType">The media type the address answered with, or null.</param>
/// <param name="Reason">An exception type name or a short note on a second look, or null; never a message.</param>
/// <param name="Ok">True when the address answered with a success status and a body that is not empty.</param>
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
/// <param name="At">When the sweep finished, UTC.</param>
/// <param name="Trigger">What started the sweep, such as roll, settled, or a request from the Admin tab.</param>
/// <param name="Version">The build version the sweep checked.</param>
/// <param name="Commit">The short commit of the build the sweep checked.</param>
/// <param name="Ms">How long the whole sweep took, in milliseconds.</param>
/// <param name="Checked">How many addresses were checked.</param>
/// <param name="Up">How many of them were up.</param>
/// <param name="Failed">The exception type name when the sweep itself stopped, or null when it ran to the end.</param>
/// <param name="Entries">Every address as the sweep found it.</param>
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
