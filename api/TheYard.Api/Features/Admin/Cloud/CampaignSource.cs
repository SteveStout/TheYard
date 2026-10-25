// What the campaign card reads (ADR-118, Decision 6): the campaign database's card schema, counts only, once a
// minute for today's figures and once an hour for the days, the leads and the Sunday table; the campaign's
// switch from the Function's health route, the same read the nightly card makes. Nothing here opens a file on
// the share, and nothing here can read a name: the site's database user may select from the card schema alone.
using System.Globalization;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The campaign card's reads.</summary>
/// <param name="counts">The card schema's reader.</param>
/// <param name="host">The Function's host name, for its health route's switches; null when it cannot be read.</param>
/// <param name="resource">The campaign database's resource id and tenant, for the header's portal link; null when the directory does not hold it.</param>
/// <param name="database">The campaign's database, by its name in configuration (Azure:Resources:CampaignDatabase).</param>
public sealed class CampaignSource(ICampaignCounts counts, Func<CancellationToken, Task<string?>> host, Func<CancellationToken, Task<(string Id, string? Tenant)?>> resource, string database)
{
    /// <summary>The campaign's database, its name on the bill.</summary>
    public string Database => database;

    /// <summary>The readings kept a minute: today's sent, written, bounced and answered.</summary>
    public static readonly IReadOnlyList<string> Readings = ["sent_today", "written_today", "bounced_today", "replies_today"];

    /// <summary>The minute: today's four counts, or none when the database did not answer.</summary>
    /// <param name="from">Unused: the counts are today's, read whole.</param>
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
        var today = Today(read, to);
        return [new ResourceMinute(to, new Dictionary<string, double?>
        {
            ["sent_today"] = today?.Sent ?? 0,
            ["written_today"] = today?.Shadow ?? 0,
            ["bounced_today"] = today?.Bounced ?? 0,
            ["replies_today"] = today?.Replies ?? 0,
        }, version)];
    }

    /// <summary>The hour: the days, the leads, the Sunday table and the switch, each a fact; a read that fails is the card's note.</summary>
    /// <param name="hour">The hour.</param>
    /// <param name="since">Unused: the campaign has no Activity Log events of its own.</param>
    /// <param name="cancellation">Stops the read.</param>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        var read = await counts.ReadAsync(cancellation);
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var (id, tenant) = await resource(cancellation) ?? ("", null);
        if (!read.Read)
        {
            facts[ResourceView.MissingFact] = read.Note;
            return new ResourceHour(hour, id, tenant, facts, []);
        }
        facts["days"] = string.Join(';', read.Days.Select(d => string.Join(',', d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Sent, d.Shadow, d.Bounced, d.Replies)));
        if (read.Leads is { } leads)
        {
            foreach (var (key, value) in new (string, int)[]
            {
                ("open", leads.Open), ("with_address", leads.WithAddress), ("waiting_address", leads.WaitingOnAddress), ("blocked", leads.Blocked),
                ("no_mx", leads.NoMx), ("unverified", leads.Unverified), ("companies", leads.Companies), ("stopped", leads.Stopped), ("boards", leads.Boards),
            })
            {
                facts[key] = value.ToString(CultureInfo.InvariantCulture);
            }
            facts["last_scan"] = leads.LastScanAt?.ToString("o", CultureInfo.InvariantCulture);
            facts["first_written"] = leads.FirstWrittenAt?.ToString("o", CultureInfo.InvariantCulture);
            facts["first_sent"] = leads.FirstSentAt?.ToString("o", CultureInfo.InvariantCulture);
            facts["last_bounce"] = leads.LastBounceAt?.ToString("o", CultureInfo.InvariantCulture);
        }
        facts["weeks"] = CloudTable.ToFact(read.Weeks
            .OrderBy(w => w.Template, StringComparer.Ordinal).ThenBy(w => w.Track, StringComparer.Ordinal).ThenBy(w => w.ProofKey, StringComparer.Ordinal).ThenBy(w => w.Weekday, StringComparer.Ordinal)
            .Select(w => new[] { $"{w.Template}, track {w.Track}", w.Weekday, w.ProofKey, $"{w.Replies} of {w.Sent} ({CampaignCard.Rate(w.Replies, w.Sent)})" }));
        var switches = await host(cancellation) is { Length: > 0 } name ? await NightlyProcesses.SwitchesAsync(name, cancellation) : null;
        facts["database"] = database;
        facts["switch"] = switches?.GetValueOrDefault("EMAIL_CAMPAIGN") ?? (switches is null ? null : "off");
        return new ResourceHour(hour, id, tenant, facts, []);
    }

    private static CampaignDay? Today(CampaignCountsRead read, DateTimeOffset at)
    {
        var day = DateOnly.FromDateTime(TheYard.Domain.Features.Admin.NightlySlots.Local(at));
        return read.Days.FirstOrDefault(d => d.Day == day);
    }
}
