using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Application;

namespace TheYard.Api;

// #region peer
/// <summary>
/// The other container, read through this one's API rather than from the
/// browser (ADR: Backends, side by side). Two reasons. Keeping the read server
/// side keeps CORS closed on both origins, and it keeps the peer's address out
/// of the client bundle: one configuration value per container holds the
/// peer's public FQDN, which is a URL and not a credential.
///
/// <para>The peer is a thing that can be down. No peer configured, no answer,
/// a slow answer and a wrong answer are four different sentences on the card,
/// and none of them is an exception: the rest of the Admin tab renders whatever
/// this returns. Two and a half seconds, not a hang, because the card refreshes
/// every thirty and a peer that takes longer than that is a peer that is
/// down for the purposes of a comparison.</para>
/// </summary>
public sealed class PeerReader(string? configuredUrl, HttpClient client)
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(2.5);

    /// <summary>The peer's origin, or null when none is configured or the deploy left its placeholder behind.</summary>
    public Uri? Url { get; } =
        string.IsNullOrWhiteSpace(configuredUrl) || configuredUrl.StartsWith("__", StringComparison.Ordinal)
            ? null
            : Uri.TryCreate(configuredUrl.TrimEnd('/'), UriKind.Absolute, out var url) ? url : null;

    public async Task<PeerView> ReadAsync()
    {
        if (Url is null)
        {
            return new PeerView(false, false, "no peer is configured on this container", null, DateTimeOffset.UtcNow, null);
        }

        string host = Url.Host;
        try
        {
            using var timeout = new CancellationTokenSource(Patience);
            using var response = await client.GetAsync(new Uri(Url, "/api/admin/metrics"), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new PeerView(true, false, $"the peer answered {(int)response.StatusCode}", host, DateTimeOffset.UtcNow, null);
            }
            var metrics = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
            return new PeerView(true, true, null, host, DateTimeOffset.UtcNow, metrics);
        }
        catch (OperationCanceledException)
        {
            return new PeerView(true, false, $"the peer did not answer within {Patience.TotalSeconds:0.#} seconds", host, DateTimeOffset.UtcNow, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            // The type, never the message: a message here carries a host name
            // and a port, and this is a public page.
            return new PeerView(true, false, $"the peer could not be read ({ex.GetType().Name})", host, DateTimeOffset.UtcNow, null);
        }
    }
}

/// <summary>What the comparison card learns about the peer: whether there is one, whether it answered, and if so its metrics as it reported them.</summary>
public sealed record PeerView(bool Configured, bool Reachable, string? Reason, string? Host, DateTimeOffset FetchedAt, JsonElement? Metrics);
// #endregion peer

// #region routes
/// <summary>
/// Timing by route rather than by path, for the rows the comparison card puts
/// side by side: a bid on one vehicle and a bid on another are the same
/// operation, and a card that listed a hundred thousand paths would say
/// nothing (ADR: Backends, side by side).
/// </summary>
public static partial class Routes
{
    [GeneratedRegex("^/api/vehicles/[^/]+")]
    private static partial Regex VehicleId();

    [GeneratedRegex("^/api/docs/diagrams/[^/]+")]
    private static partial Regex DiagramName();

    [GeneratedRegex("^/api/docs/[^/]+")]
    private static partial Regex DocSlug();

    [GeneratedRegex("^/api/images/.+")]
    private static partial Regex ImageFile();

    public static string TemplateOf(string path)
    {
        string template = VehicleId().Replace(path, "/api/vehicles/{id}");
        template = DiagramName().Replace(template, "/api/docs/diagrams/{name}");
        if (!template.StartsWith("/api/docs/diagrams/", StringComparison.Ordinal))
        {
            template = DocSlug().Replace(template, "/api/docs/{slug}");
        }
        return ImageFile().Replace(template, "/api/images/{file}");
    }

    public static string Of(string method, string path) => $"{method} {TemplateOf(path)}";

    /// <summary>Per-route timings, busiest first.</summary>
    public static IReadOnlyList<RouteTiming> ByRoute(IReadOnlyList<RequestEntry> requests) =>
        requests
            .GroupBy(entry => Of(entry.Method, entry.Path), StringComparer.Ordinal)
            .Select(group =>
            {
                long[] durations = group.Select(entry => entry.DurationMs).ToArray();
                return new RouteTiming(group.Key, durations.Length, Percentiles.Of(durations, 50), Percentiles.Of(durations, 95), durations.Max());
            })
            .OrderByDescending(timing => timing.Count)
            .ThenBy(timing => timing.Route, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// What each route costs the document store, from the operations the store
    /// log attributes to it: how many operations a request runs and the request
    /// units they add up to, as the median over the requests seen. Requests are
    /// told apart by their path string, so two bids on the same vehicle are one
    /// sample, which the card says.
    /// </summary>
    public static IReadOnlyList<RouteCharge> ChargesByRoute(IReadOnlyList<StoreOperation> operations) =>
        operations
            .Where(operation => operation.Request is not null)
            .GroupBy(operation => operation.Request!, StringComparer.Ordinal)
            .Select(perRequest =>
            {
                var parts = perRequest.Key.Split(' ', 2);
                string route = parts.Length == 2 ? Of(parts[0], parts[1]) : perRequest.Key;
                return (Route: route, Operations: perRequest.Count(), Charge: perRequest.Sum(operation => operation.RequestCharge), CrossPartition: perRequest.Count(o => o.Partition.StartsWith("cross", StringComparison.Ordinal)));
            })
            .GroupBy(sample => sample.Route, StringComparer.Ordinal)
            .Select(group =>
            {
                double[] charges = group.Select(sample => sample.Charge).OrderBy(charge => charge).ToArray();
                return new RouteCharge(
                    group.Key,
                    group.Count(),
                    Math.Round(group.Select(sample => (double)sample.Operations).Average(), 1),
                    Math.Round(charges[(int)Math.Ceiling(charges.Length * 0.5) - 1], 2),
                    Math.Round(charges[^1], 2),
                    group.Sum(sample => sample.CrossPartition));
            })
            .OrderByDescending(charge => charge.Requests)
            .ThenBy(charge => charge.Route, StringComparer.Ordinal)
            .ToArray();
}

public sealed record RouteTiming(string Route, int Count, long P50Ms, long P95Ms, long MaxMs);

public sealed record RouteCharge(string Route, int Requests, double OperationsPerRequest, double RuP50, double RuMax, int CrossPartition);
// #endregion routes
