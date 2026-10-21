// The nightly Function's card (ADR: The Nightly Function on the Admin tab): the share of its
// slots that fired on time over thirty days and the month's share of the free grant on the dials,
// a chart of its firings with every Monday and Friday shaded, four questions, and a readout of the
// job. Its readings are Azure Monitor's and GitHub's; nothing from the file share it works on is
// read, and until the site's identity holds a role on the Function's group, every line that needs
// Azure says so in words.
using System.Globalization;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>The nightly Function's card.</summary>
public static class NightlyCard
{
    /// <summary>Where each row and readout line of this card is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Last fired"] = "Azure Monitor, " + NightlyMetrics.Executions,
        ["Cost, month to date"] = CardFrame.TheBill,
    };

    /// <summary>
    /// How many share snapshots the Function keeps: ShareSnapshot.Keep in
    /// functions/jobhunt/ShareSnapshot.cs, which this project does not reference. A test reads that
    /// file and holds the two equal.
    /// </summary>
    public const int SnapshotsKept = 14;

    /// <summary>The firing dial runs the other way, so its tone is read on the misses.</summary>
    public static readonly (double Amber, double Red) Misses = CloudThresholds.NightlyMisses;

    public static readonly (double Amber, double Red) Grant = CloudThresholds.NightlyGrant;

    /// <summary>The thirty days' slots judged against the kept five-minute firing counts, from the first kept bucket so a card read on its first day is not scored on days nobody read.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static SlotTally Tally(ResourceCardInputs inputs)
    {
        (DateTimeOffset At, double Count)[] buckets = [.. inputs.Month.Select(b => (b.At, b.Sums.GetValueOrDefault("executions") ?? 0))];
        if (buckets.Length == 0)
        {
            return new SlotTally(0, 0, 0, 0);
        }
        var start = new[] { inputs.Now - TimeSpan.FromDays(30), buckets.Min(b => b.At) }.Max();
        // A slot whose hour is still under way is judged once it is over.
        return NightlySlots.Tally(NightlySlots.Due(start, inputs.Now - TimeSpan.FromHours(1)), buckets);
    }

    /// <summary>The month's execution units in gigabyte-seconds, from the first of the UTC month; null when none is kept.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static double? GbSecondsThisMonth(ResourceCardInputs inputs)
    {
        var monthStart = new DateTimeOffset(inputs.Now.UtcDateTime.Year, inputs.Now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var units = inputs.Month.Where(b => b.At >= monthStart).Select(b => b.Sums.GetValueOrDefault("units_mbms")).Where(v => v is not null).Select(v => v!.Value).ToList();
        return units.Count == 0 ? null : Math.Round(units.Sum() / NightlyMetrics.MbMsPerGbSecond, 2);
    }

    /// <summary>Every Monday and Friday in the window, in Central days, as bands behind the chart.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static IReadOnlyList<CloudBand> Bands(ResourceCardInputs inputs)
    {
        var bands = new List<CloudBand>();
        var windowStart = inputs.Now - inputs.Length;
        for (var day = NightlySlots.StartOfCentralDay(windowStart); day < inputs.Now;)
        {
            // A Central day is 23, 24 or 25 hours; twenty-five past its start is always inside the next one, or its first moment.
            var next = NightlySlots.StartOfCentralDay(day.AddHours(25));
            var weekday = NightlySlots.Local(day).DayOfWeek;
            if (weekday is DayOfWeek.Monday or DayOfWeek.Friday)
            {
                var from = day < windowStart ? windowStart : day;
                var to = next > inputs.Now ? inputs.Now : next;
                bands.Add(new CloudBand(from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), weekday.ToString()));
            }
            day = next;
        }
        return bands;
    }

    /// <param name="inputs">The card's inputs.</param>
    /// <param name="names">The Function's names, from configuration.</param>
    /// <param name="functionPath">The Function app's resource id as configuration names it, for the header link before Azure is read.</param>
    /// <param name="runs">The run log, newest first.</param>
    public static CloudReport View(ResourceCardInputs inputs, NightlyResources names, string functionPath, IReadOnlyList<NightlyRun> runs)
    {
        bool roleMissing = ResourceView.Fact(inputs, NightlySource.RoleFact, "") == NightlySource.RoleMissing;
        string noRole = NightlyRows.NoRole(names.Group);
        var tally = Tally(inputs);
        double? onTime = roleMissing ? null : tally.OnTimePercent;
        var missTone = ResourceView.Tone([onTime is { } share ? 100 - share : (double?)null], Misses, hourly: true);
        double? gbSeconds = roleMissing ? null : GbSecondsThisMonth(inputs);
        double? grantShare = ResourceView.Share(gbSeconds, CloudThresholds.FreeGrantGbSeconds);
        var grantTone = ResourceView.Tone([grantShare], Grant, hourly: true);
        var dials = new[]
        {
            new CloudDial("Runs on time, 30 days", onTime, PlanView.Percent(onTime),
                roleMissing ? noRole : tally.Due == 0 ? "no slot kept yet" : $"{tally.OnTime} of {tally.Due} slots", 100 - Misses.Amber, 100 - Misses.Red, null, null, CloudTones.Of(missTone)),
            new CloudDial("Free grant used, this month", grantShare, PlanView.Percent(grantShare),
                roleMissing ? noRole : gbSeconds is { } gb ? $"{gb.ToString("N0", CultureInfo.InvariantCulture)} of {CloudThresholds.FreeGrantGbSeconds:N0} GB-s" : NightlyRows.NoMinutes(inputs), Grant.Amber, Grant.Red, null, null, CloudTones.Of(grantTone)),
        };
        var next = NightlySlots.Next(inputs.Now);
        string? lastFired = ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("last_fired");
        string lastFiredWords = roleMissing ? noRole
            : DateTimeOffset.TryParse(lastFired, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fired) ? NightlySlots.Local(fired).ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " Central"
            : ResourceView.Latest(inputs)?.Facts.ContainsKey("state") is true ? "none in the day read"
            : NightlyRows.NoHour(inputs);
        var newest = runs.Count > 0 ? runs[0] : null;
        bool nightlyOn = runs.Any(r => r.StartedBy == GitHubRuns.ByFunction && r.Lane == "reply");
        bool bookkeepingOn = runs.Any(r => r.StartedBy == GitHubRuns.ByFunction && r.Lane == "bookkeeping");
        double? cost = NightlyRows.CostTotal(inputs);
        var readout = new CloudReadout(
            "The nightly job",
            onTime,
            $"slots on time, {CloudTones.WindowWords("30d")}",
            [
                new("Next slot", $"{NightlySlots.Local(next.At).ToString("ddd HH:mm", CultureInfo.InvariantCulture)} Central, {(next.Slot == NightlySlots.Reply ? "the reply run" : "the records run")}", "info") { Source = CardFrame.ThisProcess },
                new("Last fired", lastFiredWords, "info"),
                new("Last run on GitHub", newest is null ? "none read" : $"#{newest.Number}, {newest.Lane}, {NightlyRows.Took(newest.TookSeconds)}, {NightlyRows.CameTo(newest)}", NightlyRows.RunsTone(runs)) { Source = "GitHub Actions" },
                // An empty run log is unread, not off: "off" is said only of a list that was read.
                new("Gates", runs.Count == 0 ? $"not read yet: {NightlyRows.NoRuns(inputs)}" : $"nightly {(nightlyOn ? "on" : "off")} · bookkeeping {(bookkeepingOn ? "on" : "off")}, inferred from the runs it dispatched in 30 days", "info") { Source = "GitHub Actions" },
                new("Snapshots kept", $"up to {SnapshotsKept}; how many are kept now is not read: it needs a data role on the storage account", "info") { Source = "the Function's code" },
                new("Cost, month to date", NightlyRows.Dollars(cost), "info"),
                ResourceView.LiveRead(inputs),
            ],
            roleMissing ? $"{noRole}: Monitoring Reader and Reader there let the site's identity read the Function" : $"{inputs.Availability.Reason}, one minute each; the runs and the record hourly");
        var chart = ResourceView.Chart(inputs, "Firings and the free grant", ("executions", "Firings", 1, false), ("units_mbms", "Grant used", null, false), Grant.Red, $"teal firings, a bucket that counted one at full height · green the month's grant used · shaded Mondays and Fridays · dashed {Grant.Red} %");
        return CardFrame.Sourced(new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0 && !roleMissing,
            Note(inputs, names, roleMissing),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "The Nightly Function",
            ResourceView.Fact(inputs, "spec", "FLEX CONSUMPTION · " + (roleMissing ? noRole : NightlyRows.NoHour(inputs)).ToUpperInvariant()),
            ResourceView.Resource(inputs) is { Href: not null } read ? read : new CloudLink(names.Function, "https://portal.azure.com/#resource" + functionPath),
            dials,
            [ResourceView.Spark(inputs, "executions", "Firings, 60 min", 1), ResourceView.Spark(inputs, "units_mbms", "Units, 60 min", Limit(inputs))],
            chart with { Series = [FiringSeries(inputs), GrantSeries(inputs)], Bands = Bands(inputs) },
            NightlyRows.Rows(inputs, names, tally, missTone, runs, roleMissing),
            readout), Sources);
    }

    /// <summary>The card's note: the missing role and the missing keep each in a sentence, so the reader sees what is read live and what is not kept.</summary>
    private static string? Note(ResourceCardInputs inputs, NightlyResources names, bool roleMissing)
    {
        string? role = roleMissing ? $"The site's identity holds no role on {names.Group}, so the Function's metrics and record are not read; the run log is GitHub's and is read." : null;
        string? keep = inputs.Availability.Available ? null : $"{ResourceView.KeepReason(inputs)}; the card reads live until it is.";
        return role is null && keep is null ? ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute"))
            : string.Join(' ', new[] { role, keep is null ? null : char.ToUpperInvariant(keep[0]) + keep[1..] }.Where(part => part is not null));
    }

    /// <summary>A minute's execution units at the instance size the record names, which the sparkline is scaled to; null before the record is read.</summary>
    private static double? Limit(ResourceCardInputs inputs) =>
        double.TryParse(ResourceView.Fact(inputs, "instance_mb", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double mb) ? mb * 60_000 : null;

    /// <summary>Whether each bucket of the window counted a firing: full height when it did, the floor when it did not, a gap where nothing was kept.</summary>
    private static CloudSeries FiringSeries(ResourceCardInputs inputs) =>
        new("Firings", [.. inputs.Buckets.Select(bucket => new CloudPoint(bucket.At.ToUnixTimeMilliseconds(),
            bucket.Sums.GetValueOrDefault("executions") is { } count ? (double?)(count > 0 ? 100 : 0) : null))]);

    /// <summary>The month's grant used, as a running share through the window, so the line climbs to the dial's figure.</summary>
    private static CloudSeries GrantSeries(ResourceCardInputs inputs)
    {
        var monthStart = new DateTimeOffset(inputs.Now.UtcDateTime.Year, inputs.Now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        double before = inputs.Month.Where(b => b.At >= monthStart && b.At < (inputs.Buckets.Count > 0 ? inputs.Buckets[0].At : inputs.Now)).Sum(b => b.Sums.GetValueOrDefault("units_mbms") ?? 0);
        double running = before;
        var points = new List<CloudPoint>();
        foreach (var bucket in inputs.Buckets)
        {
            running = bucket.At < monthStart ? 0 : running + (bucket.Sums.GetValueOrDefault("units_mbms") ?? 0);
            points.Add(new CloudPoint(bucket.At.ToUnixTimeMilliseconds(), ResourceView.Share(running / NightlyMetrics.MbMsPerGbSecond, CloudThresholds.FreeGrantGbSeconds)));
        }
        return new CloudSeries("Grant used", points);
    }
}
