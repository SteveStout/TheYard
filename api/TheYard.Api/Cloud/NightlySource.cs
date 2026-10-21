// What the nightly card reads from Azure (ADR: The Nightly Function on the Admin tab): the
// Function's two Azure Monitor metrics every minute through a list of its own resource group, and
// once an hour its ARM record, the Activity Log and a day of five-minute firing counts judged
// against the slot table. The workflow's runs are NightlyRunReader's, which the hour asks first.
// The site's identity holds no role on that group until Steve grants one, so a 401 or 403 from
// Resource Manager or Monitor is an answer, not a failure: the hour says the role is missing and
// the card says "not read" on every line that needs it.
using System.Net;
using System.Text.Json;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>Reads the Function's minutes and hours for the nightly card, the workflow's runs through its run reader.</summary>
/// <param name="azure">Azure Monitor and Resource Manager, as the site's identity.</param>
/// <param name="directory">The Function's resource group, listed once an hour.</param>
/// <param name="names">The Function's names, from configuration.</param>
/// <param name="runs">The workflow's runs: read from GitHub, kept, and held for the card.</param>
/// <param name="log">Where the judged slots are kept.</param>
/// <param name="functionPath">The Function app's resource id as configuration names it, for the portal link before Azure has been read.</param>
public sealed class NightlySource(AzureMetrics azure, ResourceDirectory directory, NightlyResources names, NightlyRunReader runs, INightlyRunLog log, string functionPath)
{
    /// <summary>The Function app's Resource Manager type.</summary>
    public const string SiteType = "Microsoft.Web/sites";

    /// <summary>The fact an hour writes when Resource Manager or Monitor refused the site's identity.</summary>
    public const string RoleFact = "role", RoleMissing = "missing";

    /// <summary>The card's copy of the run log.</summary>
    public NightlyRunCache Cache => runs.Cache;

    /// <summary>The Function's names, from configuration.</summary>
    public NightlyResources Names => names;

    /// <summary>The Function app's resource id as configuration names it.</summary>
    public string FunctionPath => functionPath;

    /// <summary>True when Azure answered that the identity may not read: 401 or 403.</summary>
    /// <summary>
    /// Why the Function was not found, with what the group did list, so the card says whether the name
    /// is wrong or the group shows this identity nothing.
    /// </summary>
    /// <param name="all">What the group listed.</param>
    /// <param name="names">The configured names.</param>
    public static string Unfound(IReadOnlyList<AzureResource> all, NightlyResources names)
    {
        if (all.Count == 0)
        {
            return $"{names.Group} lists nothing to the site's identity: the group is empty, or it holds no role that reads it";
        }
        var sites = all.Where(r => string.Equals(r.Type, SiteType, StringComparison.OrdinalIgnoreCase)).Select(r => r.Name).ToList();
        string held = sites.Count == 0 ? "no web or Function app" : string.Join(", ", sites);
        return $"no {SiteType} named {names.Function} in {names.Group}, which lists {all.Count} resources and {held}";
    }

    public static bool Refused(HttpRequestException ex) => ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;

    /// <summary>The Function's executions and execution units a minute; nothing off Azure, while the role is missing, or before the Function exists.</summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return [];
        }
        try
        {
            var site = ResourceDirectory.Find(await directory.AllAsync(cancellation), SiteType, names.Function);
            return site is null ? [] : await azure.MinutesAsync(site.Id, NightlyMetrics.All, from, to, version, cancellation);
        }
        catch (HttpRequestException ex) when (Refused(ex))
        {
            // No role on the group yet: the hour records it, and the card says so.
            return [];
        }
    }

    /// <summary>The hour's facts: the runs from GitHub first, then the Function's record and its judged slots, or that the role is missing.</summary>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal) { ["github"] = await runs.ReadAsync(cancellation) };
        if (!azure.Configured)
        {
            facts["azure"] = "no identity here";
            return new ResourceHour(hour, "", null, facts, []);
        }
        try
        {
            var listed = await directory.AllAsync(cancellation);
            var site = ResourceDirectory.Find(listed, SiteType, names.Function);
            if (site is null)
            {
                facts["site"] = Unfound(listed, names);
                return new ResourceHour(hour, "", null, facts, []);
            }
            var (record, tenant) = await azure.RecordAsync(site.Id, "2024-04-01", cancellation);
            using (record)
            {
                foreach (var (key, value) in Facts(record.RootElement))
                {
                    facts[key] = value;
                }
            }
            var firings = await FiringsAsync(site.Id, hour, cancellation);
            if ((await log.AvailabilityAsync(cancellation)).Available)
            {
                await log.UpsertFiringsAsync(firings, cancellation);
            }
            facts["last_fired"] = firings.Where(f => f.FiredAt is not null).Select(f => f.FiredAt).Max()?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            return new ResourceHour(hour, site.Id, tenant, facts, await EventsAsync(site.Id, since, hour, cancellation));
        }
        catch (HttpRequestException ex) when (Refused(ex))
        {
            facts[RoleFact] = RoleMissing;
            return new ResourceHour(hour, "", null, facts, []);
        }
    }

    /// <summary>The Function's Activity Log for the hour; none when the identity may read the group but not the subscription's log.</summary>
    private async Task<IReadOnlyList<PlanEvent>> EventsAsync(string siteId, DateTimeOffset since, DateTimeOffset hour, CancellationToken cancellation)
    {
        try
        {
            return await azure.EventsAsync(siteId, since, hour, cancellation);
        }
        catch (HttpRequestException ex) when (Refused(ex))
        {
            return [];
        }
    }

    /// <summary>The day before <paramref name="hour"/> at five minutes, each due slot judged on time, late or missed.</summary>
    private async Task<IReadOnlyList<NightlyFiring>> FiringsAsync(string siteId, DateTimeOffset hour, CancellationToken cancellation)
    {
        var from = hour.AddDays(-1);
        string span = $"{from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}/{hour.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        using var answer = await azure.GetJsonAsync($"{siteId}/providers/microsoft.insights/metrics?api-version=2023-10-01&metricnames={NightlyMetrics.Executions}&interval=PT5M&timespan={span}&aggregation=total", cancellation);
        var series = PlanShapes.Series(answer.RootElement, [(NightlyMetrics.Executions, "total")]);
        var buckets = new List<(DateTimeOffset At, double Count)>();
        if (series.TryGetValue(NightlyMetrics.Executions, out var points))
        {
            foreach (var (at, count) in points)
            {
                buckets.Add((at, count));
            }
        }
        return [.. NightlySlots.Due(from, hour).Select(due =>
        {
            var (outcome, firedAt) = NightlySlots.Judge(due.At, buckets);
            return new NightlyFiring(due.At, due.Slot, outcome, firedAt);
        })];
    }

    /// <summary>The Function's facts from its ARM record: its state, its runtime and its instance size, and the spec line they make.</summary>
    public static IReadOnlyDictionary<string, string?> Facts(JsonElement record)
    {
        string? runtime = AzureMetrics.Text(record, "properties.functionAppConfig.runtime.name");
        string? runtimeVersion = AzureMetrics.Text(record, "properties.functionAppConfig.runtime.version");
        string? memory = AzureMetrics.Text(record, "properties.functionAppConfig.scaleAndConcurrency.instanceMemoryMB");
        string? location = AzureMetrics.Text(record, "location");
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["state"] = AzureMetrics.Text(record, "properties.state"),
            ["instance_mb"] = memory,
            ["spec"] = string.Join(' ', new[] { "FLEX CONSUMPTION", runtime is null ? null : $"{runtime} {runtimeVersion}".Trim().ToUpperInvariant(), memory is null ? null : memory + " MB", location?.ToUpperInvariant() }.Where(part => part is not null)),
        };
    }
}
