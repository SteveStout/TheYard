// The cost card's "What watches the meters that can run away?" (ADR: What Azure charges, the addendum on the
// spike guard): one row per meter this site can run up, each joined by name to the rule Azure holds for it.
// Facts only: whether the rule exists and is on, and its line; the tone each state reads is the page's.
namespace TheYard.Api.Shared.CostReadings;

/// <summary>One meter and the rule that watches it.</summary>
/// <param name="Meter">The meter, as a person would name it.</param>
/// <param name="Watches">The resource whose meter it is.</param>
/// <param name="Rule">The rule's name in Azure.</param>
/// <param name="State">"exists", "off", "not set" or "not read yet".</param>
/// <param name="Line">The rule's line as Azure holds it, or why there is none.</param>
public sealed record MeterRow(string Meter, string Watches, string Rule, string State, string Line);

/// <summary>The meters this site names, joined to the last read of the rules.</summary>
public static class MeterRows
{
    /// <summary>Every meter that can run away and the rule that watches it, in the order the card lists them.</summary>
    public static readonly IReadOnlyList<(string Meter, string Watches, string Rule)> Named =
    [
        ("File transactions", "the job-hunt share", "alert-jobhunt-share-transactions"),
        ("Blob transactions", "the job-hunt account's blobs", "alert-jobhunt-blob-transactions"),
        ("Blob egress", "the job-hunt account's blobs", "alert-jobhunt-blob-egress"),
        ("Log Analytics ingestion", "the site's log workspace, against its daily cap", "alert-theyard-ingestion"),
        ("Email sends", "the site's mail service", "alert-theyard-email-sends"),
        ("Function executions", "the job-hunt Function", "alert-jobhunt-function-executions"),
        ("Bandwidth out", "the site's web plan", "alert-theyard-bytes-sent"),
        ("The budget", "the subscription", "budget-subscription-monthly"),
        ("Cost anomalies", "the subscription", "anomaly-subscription-daily"),
    ];

    /// <summary>The words a meter reads before the rules have been read.</summary>
    public const string NotRead = "not read yet: the rules are read an hour apart, beside the bill";

    /// <summary>
    /// One row per named meter: "exists" with the rule's line when it is found and on, "off" when it is
    /// found and switched off, "not set" when it is not found (with why, when part of the read was refused),
    /// and "not read yet" before the first read. Never empty, never null.
    /// </summary>
    public static IReadOnlyList<MeterRow> From(AlertRulesRead? read) =>
    [
        .. Named.Select(meter =>
        {
            if (read is null)
            {
                return new MeterRow(meter.Meter, meter.Watches, meter.Rule, "not read yet", NotRead);
            }

            if (read.Found.TryGetValue(meter.Rule, out var rule))
            {
                return new MeterRow(meter.Meter, meter.Watches, meter.Rule, rule.Enabled ? "exists" : "off", rule.Line);
            }

            string why = read.Notes.Count > 0 ? string.Join("; ", read.Notes) : "no rule by this name: it waits for its deploy";
            return new MeterRow(meter.Meter, meter.Watches, meter.Rule, "not set", why);
        }),
    ];
}
