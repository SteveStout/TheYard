// The Communication Services card (ADR: One frame for every resource): sends this hour against
// the free sender's hourly limit and the bounce rate on the dials, what uses it, whether mail is
// arriving, what Azure did to it, and whether the Azure-managed sender still fits.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The mail card.</summary>
public static class MailCard
{
    /// <summary>
    /// What the Azure-managed sender allows an hour. Typed, with <see cref="CustomDomainPerHour"/>,
    /// because no record the service returns states its sending limit; which of the two the dial is
    /// read against follows the domain's own domainManagement, read each hour.
    /// </summary>
    public const double SendsPerHour = 100;

    /// <summary>What a custom domain's sender allows an hour.</summary>
    public const double CustomDomainPerHour = 1_800;

    /// <summary>The hourly limit of the sender the domain record names: the Azure-managed one unless the record says custom.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static double LimitOf(ResourceCardInputs inputs) =>
        ResourceView.Fact(inputs, "domain_kind", "") == "a custom domain" ? CustomDomainPerHour : SendsPerHour;

    /// <summary>Filled in from the resource's metric definitions; see <see cref="Metrics"/>.</summary>
    public static IReadOnlyList<AzureMetric> Metrics { get; } = MailMetrics.All;

    public static readonly (double Amber, double Red) Sends = CloudThresholds.MailSends;

    /// <summary>The bounce rate, in percent of delivery outcomes.</summary>
    public static readonly (double Amber, double Red) Bounces = CloudThresholds.MailBounces;

    public static IReadOnlyList<string> Readings => [.. Metrics.Select(m => m.Reading).Distinct()];

    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? sendsHour = ResourceView.HourSum(inputs, "sends");
        double limit = LimitOf(inputs);
        double? delivered = ResourceView.WindowSum(inputs, "delivered"), bounced = ResourceView.WindowSum(inputs, "bounced"),
            suppressed = ResourceView.WindowSum(inputs, "suppressed"), quarantined = ResourceView.WindowSum(inputs, "quarantined"), failed = ResourceView.WindowSum(inputs, "failed");
        double outcomes = (delivered ?? 0) + (bounced ?? 0) + (suppressed ?? 0) + (quarantined ?? 0) + (failed ?? 0);
        double? bounceRate = outcomes > 0 ? Math.Round(100 * ((bounced ?? 0) + (failed ?? 0)) / outcomes, 2) : null;
        var sendsTone = PlanThresholds.ToneOf([ResourceView.Share(sendsHour, limit)], Sends);
        var bounceTone = bounceRate is { } br ? (br >= Bounces.Red ? PlanTone.Alarm : br >= Bounces.Amber ? PlanTone.Warning : PlanTone.Good) : PlanTone.Info;
        var dials = new[]
        {
            new CloudDial("Sends, this hour", ResourceView.Share(sendsHour, limit), PlanView.Percent(ResourceView.Share(sendsHour, limit)),
                sendsHour is { } s ? $"{s.ToString("0", CultureInfo.InvariantCulture)} of {limit:N0} an hour" : "not read", Sends.Amber, Sends.Red, null, null, CloudTones.Of(sendsTone)),
            new CloudDial("Bounce rate", bounceRate, bounceRate is null ? "none" : PlanView.Percent(bounceRate),
                outcomes > 0 ? $"{ResourceView.Number((bounced ?? 0) + (failed ?? 0))} of {outcomes:0} outcomes" : "no mail in the window", Bounces.Amber, Bounces.Red, null, null, CloudTones.Of(bounceTone)),
        };
        var events = inputs.Hours.SelectMany(h => h.Events).ToList();
        var rows = new List<CloudRow>
        {
            new("uses", "What uses it?", "password reset links, from the site",
                [new($"sender {ResourceView.Fact(inputs, "sender_domain")} · {ResourceView.Fact(inputs, "domain_kind")}", null)], "good"),
            new("arriving", "Is mail arriving?", outcomes == 0 ? "no mail in the window" : $"{ResourceView.Number(delivered)} delivered of {outcomes:0}",
                [new($"{ResourceView.Number(bounced)} bounced · {ResourceView.Number(suppressed)} suppressed · {ResourceView.Number(quarantined)} quarantined · {ResourceView.Number(failed)} failed", null)],
                CloudTones.Of(bounceTone)),
            new("azure", "What did Azure do to it?", events.Count == 0 ? "nothing in the window" : $"{events.Count} events",
                [.. events.Take(3).Select(e => new CloudLink($"{e.At:MMM d HH:mm} {e.Title}", null))], events.Any(e => !e.Planned) ? "warning" : "good"),
            new("sender", $"Is {ResourceView.Fact(inputs, "domain_kind", "the sender")} still right?", sendsTone == PlanTone.Alarm ? "no, sends are near its hourly limit" : "yes",
                [new($"{ResourceView.Number(sendsHour)} sends this hour of {limit:N0} · the Azure-managed sender allows {SendsPerHour:0} an hour, a custom domain {CustomDomainPerHour:N0}", null)],
                sendsTone == PlanTone.Alarm ? "alarm" : "good"),
        };
        var readout = new CloudReadout(
            "Communication Services",
            ResourceView.Uptime(inputs),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Sends, hour", ResourceView.Number(sendsHour), CloudTones.Of(sendsTone)),
                new("Sends, window", ResourceView.Number(ResourceView.WindowSum(inputs, "sends")), "info"),
                new("Delivered", ResourceView.Number(delivered), "info"),
                new("Bounced", ResourceView.Number(bounced), bounced is > 0 ? "warning" : "info"),
                new("Suppressed", ResourceView.Number(suppressed), "info"),
                new("Quarantined", ResourceView.Number(quarantined), quarantined is > 0 ? "warning" : "info"),
                new("Failed", ResourceView.Number(failed), failed is > 0 ? "warning" : "info"),
                new("Bounce rate", bounceRate is { } r ? $"{r.ToString("0.0", CultureInfo.InvariantCulture)} %" : "none", CloudTones.Of(bounceTone)),
                new("Sender", ResourceView.Fact(inputs, "sender_domain"), "info"),
                new("Data location", ResourceView.Fact(inputs, "data_location"), "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            $"{inputs.Availability.Reason}, one minute each; the domain hourly");
        return new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Communication Services",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "sends", "Sends, 60 min", limit / 60), ResourceView.Spark(inputs, "delivered", "Delivered, 60 min", limit / 60)],
            $"Normal: sends amber {Sends.Amber} %, red {Sends.Red} % of {limit:N0} an hour; bounces amber {Bounces.Amber} %, red {Bounces.Red} %.",
            ResourceView.Chart(inputs, "Sends and deliveries", ("sends", "Sends", 10, false), ("delivered", "Delivered", 10, false), 100, "teal sends · green delivered, as a share of 10 a minute · gold deploys · red event"),
            rows,
            readout);
    }

    /// <summary>The hour's facts from the resource's record and its linked email domain.</summary>
    public static IReadOnlyDictionary<string, string?> Facts(System.Text.Json.JsonElement record, System.Text.Json.JsonElement? domain)
    {
        string? name = domain is { } d ? AzureMetrics.Text(d, "name") : null;
        string? managed = domain is { } d2 ? AzureMetrics.Text(d2, "properties.domainManagement") : null;
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["sender_domain"] = domain is { } d3 ? AzureMetrics.Text(d3, "properties.mailFromSenderDomain") ?? name : null,
            ["domain_kind"] = managed == "AzureManaged" ? "the free Azure-managed sender" : managed is null ? null : "a custom domain",
            ["data_location"] = AzureMetrics.Text(record, "properties.dataLocation"),
            ["spec"] = string.Join(' ', new[] { "PAY PER MESSAGE", AzureMetrics.Text(record, "properties.dataLocation")?.ToUpperInvariant() }.Where(p => p is not null)),
        };
    }
}
