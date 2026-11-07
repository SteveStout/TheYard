// What the network card reads (ADR-124): the card schema's Network view, counts
// only, once a minute for the drafts waiting and once an hour for the weeks and the states; the switch from the
// Function's health route, the same read the nightly and campaign cards make; and the newest pass the Function's run
// history lists, kept as the hour's facts the way the nightly card keeps its runs, so the logging row says what the
// pass came to and not only what the switch says. Nothing here can read a name: the site's database user may select
// from the card schema alone.
using System.Globalization;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The network card's reads.</summary>
/// <param name="counts">The card schema's reader.</param>
/// <param name="host">The Function's host name, for its health route's switches; null when it cannot be read.</param>
/// <param name="resource">The database's resource id and tenant, for the header's portal link; null when the directory does not hold it.</param>
/// <param name="database">The database the network schema lives in, by its name in configuration.</param>
/// <param name="passes">The Function that runs the pass and its run history; null where neither is read, and the card then says the pass was not read.</param>
public sealed class NetworkSource(INetworkCounts counts, Func<CancellationToken, Task<string?>> host, Func<CancellationToken, Task<(string Id, string? Tenant)?>> resource, string database, NetworkPasses? passes = null)
{

    /// <summary>The database, its name on the bill.</summary>
    public string Database => database;

    /// <summary>The readings kept a minute: drafts waiting, rows open, conversations started this week.</summary>
    public static readonly IReadOnlyList<string> Readings = ["drafted", "open", "started_week"];

    /// <summary>The minute: the three counts, or none when the database did not answer.</summary>
    /// <param name="from">Unused: the counts are read whole.</param>
    /// <param name="to">The minute.</param>
    /// <param name="version">The site's version, kept with the minute.</param>
    /// <param name="cancellation">Stops the read.</param>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        var read = await counts.ReadAsync(cancellation);
        if (!read.Read)
        {
            return [];
        }
        var monday = NetworkCard.Monday(DateOnly.FromDateTime(TheYard.Domain.Features.Admin.NightlySlots.Local(to)));
        return [new ResourceMinute(to, new Dictionary<string, double?>
        {
            ["drafted"] = read.States.GetValueOrDefault("Drafted"),
            ["open"] = read.States.GetValueOrDefault("Open"),
            ["started_week"] = read.Weeks.Where(w => w.Monday == monday).Sum(w => w.Started),
        }, version)];
    }

    /// <summary>The hour: the weeks, the states and the switch, each a fact; a read that fails is the card's note.</summary>
    /// <param name="hour">The hour.</param>
    /// <param name="since">Unused: the network log has no Activity Log events of its own.</param>
    /// <param name="cancellation">Stops the read.</param>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        var read = await counts.ReadAsync(cancellation);
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var (id, tenant) = await resource(cancellation) ?? ("", null);
        facts["function"] = passes?.Function;
        foreach (var (key, value) in await (passes ?? NetworkPasses.None).PassAsync(cancellation))
        {
            facts[key] = value;
        }
        if (!read.Read)
        {
            facts[ResourceView.MissingFact] = read.Note;
            return new ResourceHour(hour, id, tenant, facts, []);
        }
        facts["weeks"] = string.Join(';', read.Weeks.Select(w => string.Join(',', w.Monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), w.Started)));
        foreach (string state in NetworkCard.States)
        {
            facts[state.ToLowerInvariant()] = read.States.GetValueOrDefault(state).ToString(CultureInfo.InvariantCulture);
        }
        var switches = await host(cancellation) is { Length: > 0 } name ? await NightlyProcesses.SwitchesAsync(name, cancellation) : null;
        facts["database"] = database;
        facts["switch"] = switches?.GetValueOrDefault("NETWORK_LOG") ?? (switches is null ? null : "off");
        return new ResourceHour(hour, id, tenant, facts, []);
    }
}
