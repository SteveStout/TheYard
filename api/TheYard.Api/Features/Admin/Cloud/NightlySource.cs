// What the nightly card reads from Azure (ADR: The Nightly Function on the Admin tab): the
// Function's two Azure Monitor metrics every minute through a list of its own resource group, and
// once an hour its ARM record, the Activity Log and a day of five-minute firing counts judged
// against the slot table. The workflow's runs are NightlyRunReader's, which the hour asks first.
// The site's identity holds no role on that group until the owner grants one, so a 401 or 403 from
// Resource Manager or Monitor is an answer, not a failure: the hour says the role is missing and
// the card says "not read" on every line that needs it.
using System.Net;
using System.Text.Json;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

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

    /// <summary>
    /// The Function's default host name as its ARM record gave it, which the merger's health is read
    /// from each minute; null until a minute or an hour has read the record. Volatile because the hour's
    /// read and the minute's run on the recorder's loop one after the other, and a reader may ask between.
    /// </summary>
    private volatile string? _host;

    /// <summary>The Function's default host name, once a minute or an hour has read it.</summary>
    public string? Host => _host;

    /// <summary>The card's copy of the run log.</summary>
    public NightlyRunCache Cache => runs.Cache;

    /// <summary>The Function's names, from configuration.</summary>
    public NightlyResources Names => names;

    /// <summary>The Function app's resource id as configuration names it.</summary>
    public string FunctionPath => functionPath;

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

    /// <summary>The sentence for an empty listing once the Function's own path has been asked: 403 no role, 404 no such name.</summary>
    /// <param name="status">What Resource Manager answered for the Function's path.</param>
    /// <param name="names">The configured names.</param>
    public static string Unlisted(HttpStatusCode status, NightlyResources names) => status is HttpStatusCode.NotFound
        ? $"no Function app named {names.Function} in {names.Group}"
        : $"the site's identity holds no role on {names.Group}";

    /// <summary>True when Azure answered that the identity may not read (401 or 403), or that the group is not there to read (404).</summary>
    public static bool Refused(HttpRequestException ex) => ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound;

    /// <summary>
    /// The Function's executions and execution units a minute, with the tracker merger's health on the
    /// newest (MergerReading); no metrics off Azure, while the role is missing, or before the Function
    /// exists. The first minute to find the Function reads its record once for the host name, so the
    /// merger row is read from the first minute after a roll, not from the first hour.
    /// </summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return [];
        }
        IReadOnlyList<ResourceMinute> minutes;
        try
        {
            var site = ResourceDirectory.Find(await directory.AllAsync(cancellation), SiteType, names.Function);
            minutes = site is null ? [] : await azure.MinutesAsync(site.Id, NightlyMetrics.All, from, to, version, cancellation);
            if (site is not null && _host is null)
            {
                _host = await HostAsync(site.Id, cancellation);
            }
        }
        catch (HttpRequestException ex) when (Refused(ex))
        {
            // No role on the group yet: the hour records it, and the card says so.
            minutes = [];
        }
        var merger = _host is { Length: > 0 } host ? await MergerReading.ReadAsync(host, cancellation) : null;
        return MergerReading.Fold(minutes, merger, to, version);
    }

    /// <summary>The Function's host name off its record; null when unread, which costs the merger reading, never the metrics.</summary>
    private async Task<string?> HostAsync(string siteId, CancellationToken cancellation)
    {
        try
        {
            using var record = (await azure.RecordAsync(siteId, "2024-04-01", cancellation)).Record;
            return AzureMetrics.Text(record.RootElement, "properties.defaultHostName");
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The hour's facts: the runs from GitHub first, then the Function's record and its judged slots, or that the role is missing.</summary>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            facts["github"] = await runs.ReadAsync(cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // The run log's own store failed under the reader: the Azure facts are still this hour's.
            facts["github"] = $"not read: the run log failed ({ex.GetType().Name})";
        }
        if (!azure.Configured)
        {
            facts["azure"] = "no identity here";
            return new ResourceHour(hour, "", null, facts, []);
        }
        try
        {
            var listed = await directory.AllAsync(cancellation);
            string? siteId = ResourceDirectory.Find(listed, SiteType, names.Function)?.Id;
            if (siteId is null && listed.Count > 0)
            {
                facts["site"] = Unfound(listed, names);
                return new ResourceHour(hour, "", null, facts, []);
            }
            JsonDocument record;
            string? tenant;
            try
            {
                // An empty listing cannot tell a missing role from a missing name; one read of the
                // Function's own path can: 403 the role, 404 the name, 200 it is there.
                (record, tenant) = await azure.RecordAsync(siteId ?? functionPath, "2024-04-01", cancellation);
                siteId ??= functionPath;
            }
            catch (HttpRequestException ex) when (siteId is null && ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
            {
                facts["site"] = Unlisted(ex.StatusCode!.Value, names);
                if (ex.StatusCode is not HttpStatusCode.NotFound)
                {
                    facts[RoleFact] = RoleMissing;
                }
                return new ResourceHour(hour, "", null, facts, []);
            }
            using (record)
            {
                foreach (var (key, value) in Facts(record.RootElement))
                {
                    facts[key] = value;
                }
                _host = AzureMetrics.Text(record.RootElement, "properties.defaultHostName") ?? _host;
            }
            var firings = await FiringsAsync(siteId, hour, cancellation);
            if ((await log.AvailabilityAsync(cancellation)).Available)
            {
                await log.UpsertFiringsAsync(firings, cancellation);
            }
            facts["last_fired"] = firings.Where(f => f.FiredAt is not null).Select(f => f.FiredAt).Max()?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            facts[SnapshotFact] = await SnapshotsAsync(siteId, cancellation);
            facts[SlotsFact] = SlotsWords(firings);
            return new ResourceHour(hour, siteId, tenant, facts, await EventsAsync(siteId, since, hour, cancellation));
        }
        catch (HttpRequestException ex) when (Refused(ex))
        {
            facts[RoleFact] = RoleMissing;
            return new ResourceHour(hour, "", null, facts, []);
        }
    }

    /// <summary>The fact an hour writes for the day of slots it judged from Azure Monitor's five-minute counts: due, on time, late, missed.</summary>
    public const string SlotsFact = "slots";

    /// <summary>The judged slots of the day read, as the slots fact writes them: "due on-time late missed".</summary>
    /// <param name="firings">The day's slots, judged.</param>
    public static string SlotsWords(IReadOnlyList<NightlyFiring> firings) => string.Join(' ',
        firings.Count,
        firings.Count(f => f.Outcome == NightlySlots.OnTime),
        firings.Count(f => f.Outcome == NightlySlots.Late),
        firings.Count(f => f.Outcome != NightlySlots.OnTime && f.Outcome != NightlySlots.Late));

    /// <summary>The fact an hour writes for how many share snapshots the storage account holds: a count, or "not read: " and why.</summary>
    public const string SnapshotFact = "snapshots";

    /// <summary>
    /// How many share snapshots the Function's storage account holds, from Resource Manager's list of
    /// its shares with their snapshots: Reader on the group reads it, so no data role and no account
    /// key is needed, and nothing in a share is opened.
    /// </summary>
    private async Task<string> SnapshotsAsync(string siteId, CancellationToken cancellation)
    {
        int providers = siteId.IndexOf("/providers/", StringComparison.OrdinalIgnoreCase);
        string group = providers < 0 ? siteId : siteId[..providers];
        try
        {
            using var answer = await azure.GetJsonAsync($"{group}/providers/Microsoft.Storage/storageAccounts/{names.Storage}/fileServices/default/shares?api-version=2023-05-01&$expand=snapshots", cancellation);
            return SnapshotCount(answer.RootElement).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (HttpRequestException ex)
        {
            return $"not read: Resource Manager answered {(ex.StatusCode is { } status ? ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture) : "nothing")} for the shares of {names.Storage}";
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // A timeout or an answer of the wrong shape costs this one line, never the hour's other facts.
            return $"not read: the shares of {names.Storage} did not answer ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// The snapshots of the share that holds the most, in a list of shares: the entries that carry a
    /// snapshot time, counted by share name. The Function snapshots its one share; a second share
    /// with snapshots of its own is not counted as the Function's.
    /// </summary>
    /// <param name="shares">Resource Manager's answer for fileServices/default/shares with $expand=snapshots.</param>
    public static int SnapshotCount(JsonElement shares) =>
        shares.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(share => AzureMetrics.Text(share, "properties.snapshotTime") is { Length: > 0 })
                .GroupBy(share => AzureMetrics.Text(share, "name") ?? "", StringComparer.Ordinal)
                .Select(group => group.Count())
                .DefaultIfEmpty(0)
                .Max()
            : 0;

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
