// The Netlify edge card (ADR: One frame for every resource): credits spent of the free plan's 300
// this cycle and the share of sampled pages the edge served from its cache on the dials, the
// certificate it answers with, what it is costing, and the date it is meant to go. Netlify's
// figures move hourly, so the card keeps one document an hour; without a token it says so.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The edge's card.</summary>
public static class EdgeCard
{
    /// <summary>The project's page on Netlify, which the card's name links to.</summary>
    public const string ProjectUrl = "https://app.netlify.com/projects/theyard-edge";

    /// <summary>When the edge is meant to retire: the registrar transfer that moves the domain off it.</summary>
    public static readonly DateOnly Retires = new(2026, 10, 30);

    public static readonly (double Amber, double Red) Credits = CloudThresholds.EdgeCredits;

    /// <summary>The cache dial runs the other way, so its tone is read on the misses.</summary>
    public static readonly (double Amber, double Red) Misses = CloudThresholds.EdgeMisses;

    public static readonly IReadOnlyList<string> Readings = ["credits", "bandwidth_gb", "deploys", "cache_hit"];

    public static CloudReport View(ResourceCardInputs inputs, bool configured)
    {
        double? credits = ResourceView.Now(inputs, "credits"), hit = ResourceView.Now(inputs, "cache_hit");
        var creditTone = ResourceView.Tone(ResourceView.Hour(inputs, "credits").Select(v => ResourceView.Share(v, NetlifyEdge.FreeCredits)).ToList(), Credits, hourly: true);
        var missTone = ResourceView.Tone(ResourceView.Hour(inputs, "cache_hit").Select(v => v is { } h ? 100 - h : (double?)null).ToList(), Misses, hourly: true);
        var dials = new[]
        {
            ResourceView.Dial(inputs, "credits", "Credits, this cycle", NetlifyEdge.FreeCredits, Credits, v => v is { } c ? $"{c.ToString("0", CultureInfo.InvariantCulture)} of {NetlifyEdge.FreeCredits:0}" : "not read", hourly: true),
            ResourceView.Dial(inputs, "cache_hit", "Cache hits", 100, (100 - Misses.Amber, 100 - Misses.Red), v => v is { } h ? $"{h.ToString("0", CultureInfo.InvariantCulture)} % of {NetlifyEdge.Sampled.Length} sampled" : "not sampled yet", percentReading: true, hourly: true) with { Tone = CloudTones.Of(missTone) },
        };
        int daysLeft = Retires.DayNumber - DateOnly.FromDateTime(inputs.Now.UtcDateTime).DayNumber;
        string? cert = ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("cert_expires");
        bool certSoon = DateTimeOffset.TryParse(cert, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires) && expires - inputs.Now < TimeSpan.FromDays(14);
        // The date a reader reads, not the timestamp Netlify sends.
        string expiresOn = cert is not null && DateTimeOffset.TryParse(cert, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var on) ? on.UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "not read";
        var rows = new List<CloudRow>
        {
            new("through", "What goes through it?", inputs.Hours.Count == 0 ? (configured ? "not read yet" : "no token set") : ResourceView.Fact(inputs, "domain"),
                [new($"certificate for {ResourceView.Fact(inputs, "cert_names")} · expires {expiresOn} · the page and /api proxied to the two web apps", null)],
                certSoon ? "warning" : "good"),
            new("credits", "Is it costing credits?", credits is { } c ? $"{c.ToString("0", CultureInfo.InvariantCulture)} of {NetlifyEdge.FreeCredits:0} this cycle" : configured ? "not read yet" : "no token set",
                [new($"{ResourceView.Number(ResourceView.Now(inputs, "deploys"))} deploys at {NetlifyEdge.CreditsPerDeploy:0} · {ResourceView.Number(ResourceView.Now(inputs, "bandwidth_gb"), "0.000")} GB at {NetlifyEdge.CreditsPerGb:0} a GB", null),
                    new("the deploy log on Netlify", ProjectUrl + "/deploys")],
                ResourceView.Fact(inputs, "deploys_failed", "0") != "0" ? "warning" : CloudTones.Of(creditTone))
            { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("deploy_rows"), "When", "Type", "Came to") },
            new("cached", "What is cached?", hit is { } h ? $"{h.ToString("0", CultureInfo.InvariantCulture)} % of the sampled pages" : "not sampled yet",
                [new($"sampled hourly: {string.Join(", ", NetlifyEdge.Sampled)} · read from the Cache-Status header", null)],
                CloudTones.Of(missTone)),
            new("needed", "Is it still needed?", daysLeft > 0 ? $"until {Retires.ToString("MMM d", CultureInfo.InvariantCulture)}, {daysLeft} days" : "past its date; retire it",
                [new("the registrar transfer moves the domain straight to Azure, and then this card retires with the edge", null)],
                daysLeft > 0 ? "info" : "warning"),
        };
        var readout = new CloudReadout(
            "The edge",
            ResourceView.Uptime(inputs, 60),
            $"answered, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Credits", $"{ResourceView.Number(credits, "0")} / {NetlifyEdge.FreeCredits:0}", CloudTones.Of(creditTone)),
                new("Credits p95, 30 d", ResourceView.Number(ResourceFolding.Spread(inputs.Month, "credits").P95, "0"), "info"),
                new("Deploys, cycle", ResourceView.Number(ResourceView.Now(inputs, "deploys")), "info"),
                new("Bandwidth, cycle", $"{ResourceView.Number(ResourceView.Now(inputs, "bandwidth_gb"), "0.000")} GB", "info"),
                new("Cache hits", hit is { } ch ? $"{ch.ToString("0", CultureInfo.InvariantCulture)} %" : "not sampled", CloudTones.Of(missTone)),
                new("Domain", ResourceView.Fact(inputs, "domain"), "info"),
                new("Certificate", ResourceView.Fact(inputs, "cert_names"), "info"),
                new("Expires", expiresOn, certSoon ? "warning" : "info"),
                new("Builds", $"{ResourceView.Fact(inputs, "deploys_live", "0")} live · {ResourceView.Fact(inputs, "deploys_skipped", "0")} skipped · {ResourceView.Fact(inputs, "deploys_failed", "0")} failed", ResourceView.Fact(inputs, "deploys_failed", "0") != "0" ? "warning" : "info"),
                new("Retires", Retires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "info"),
                new("Cost", "not on the Azure bill; Netlify bills in credits", "info"),
                ResourceView.LiveRead(inputs, hourly: true),
            ],
            configured ? $"{inputs.Availability.Reason}, one document an hour from Netlify" : "NETLIFY_TOKEN is not set, so nothing is read from Netlify");
        return new CloudReport(
            inputs.Window, PlanView.Windows, configured && inputs.Availability.Available && inputs.LastHour.Count > 0,
            !configured ? "NETLIFY_TOKEN is not set, so the edge is not read."
                : ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("site") is { } missing ? $"NETLIFY_TOKEN is set, but {missing}, so the edge is not read."
                : ResourceView.Note(inputs, "no hour kept yet; the first is read a minute after the SQL site starts"),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Netlify edge",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("plan") is { } plan ? $"{plan.ToUpperInvariant()} PLAN · 300 CREDITS" : "FREE PLAN · 300 CREDITS",
            new CloudLink("theyard-edge", ProjectUrl),
            dials,
            [ResourceView.Spark(inputs, "credits", "Credits, 60 h", NetlifyEdge.FreeCredits), ResourceView.Spark(inputs, "cache_hit", "Cache hits, 60 h", 100, percentReading: true)],
            $"Normal: credits amber {Credits.Amber} %, red {Credits.Red} % of 300; cache misses amber {Misses.Amber} %, red {Misses.Red} %; held 15 min.",
            ResourceView.Chart(inputs, "Credits and cache hits", ("credits", "Credits", NetlifyEdge.FreeCredits, false), ("cache_hit", "Cache hits", 100, true), Credits.Red, "teal credits · green cache hits · gold deploys · dashed 85 %"),
            rows,
            readout);
    }
}
