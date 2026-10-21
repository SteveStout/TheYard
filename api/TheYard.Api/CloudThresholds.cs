// Every resource card's lines in one place (ADR: One frame for every resource): the two scarce
// numbers each card dials, where each turns amber and red, as a percentage of its limit. Each
// level counts only when fifteen minutes in a row hold it (PlanThresholds.ToneOf), so a deploy's
// warm-up reads as a spike; CloudThresholdTests holds every number here.
namespace TheYard.Api;

// #region cloud-thresholds
/// <summary>The amber and red line of each resource card's two dials, in per cent.</summary>
public static class CloudThresholds
{
    /// <summary>Azure SQL Database: DTU of the five, amber at 60 per cent and red at 90.</summary>
    public static readonly (double Amber, double Red) SqlDtu = (60, 90);

    /// <summary>Azure SQL Database: data space of the 2 GB, amber at 70 and red at 85.</summary>
    public static readonly (double Amber, double Red) SqlSpace = (70, 85);

    /// <summary>Cosmos DB: request units a second of the 1,000 shared, amber at 70 and red at 90.</summary>
    public static readonly (double Amber, double Red) CosmosThroughput = (70, 90);

    /// <summary>Cosmos DB: storage of the free 25 GB, amber at 70 and red at 90.</summary>
    public static readonly (double Amber, double Red) CosmosStorage = (70, 90);

    /// <summary>Container Registry: storage of the 10 GB Basic includes, amber at 70 and red at 90.</summary>
    public static readonly (double Amber, double Red) RegistryStorage = (70, 90);

    /// <summary>Container Registry: pulls this minute of the 1,000 Basic allows, amber at 60 and red at 90.</summary>
    public static readonly (double Amber, double Red) RegistryPulls = (60, 90);

    /// <summary>Application Insights: today's ingestion against the daily cap, amber at 70 and red at 90; a capped hour is red at once, outside these.</summary>
    public static readonly (double Amber, double Red) InsightsIngestion = (70, 90);

    /// <summary>Application Insights: what sampling dropped, amber at 50 per cent and red at 90.</summary>
    public static readonly (double Amber, double Red) InsightsDropped = (50, 90);

    /// <summary>Communication Services: sends this hour of the free sender's 100, amber at 60 and red at 85.</summary>
    public static readonly (double Amber, double Red) MailSends = (60, 85);

    /// <summary>Communication Services: bounces of the delivery outcomes, amber at 5 per cent and red at 10, read on the window rather than held.</summary>
    public static readonly (double Amber, double Red) MailBounces = (5, 10);

    /// <summary>The Netlify edge: credits of the 300 this cycle, amber at 60 and red at 85.</summary>
    public static readonly (double Amber, double Red) EdgeCredits = (60, 85);

    /// <summary>The Netlify edge: cache misses of the sampled pages, amber at 50 and red at 80.</summary>
    public static readonly (double Amber, double Red) EdgeMisses = (50, 80);

    /// <summary>
    /// The nightly Function: slots that did not fire on time over thirty days, amber at 5 per cent and
    /// red at 10. The dial shows the share on time, a floor (amber under 95, red under 90), so its tone
    /// is read on the misses the way the edge's cache dial is. Source: the levels Steve set for the card
    /// (PROMPT-THEYARD-NIGHTLY-FUNCTION-CARD): one late or missed slot in twenty is worth a look, one in ten is broken.
    /// </summary>
    public static readonly (double Amber, double Red) NightlyMisses = (5, 10);

    /// <summary>
    /// The nightly Function: the month's execution units against the Flex Consumption free grant,
    /// amber at 80 per cent and red at 95. Source: the levels Steve set for the card
    /// (PROMPT-THEYARD-NIGHTLY-FUNCTION-CARD); the grant is per subscription and resets on the first of the month.
    /// </summary>
    public static readonly (double Amber, double Red) NightlyGrant = (80, 95);

    /// <summary>
    /// Azure Functions pricing page, Flex Consumption on-demand free grant, per subscription: gigabyte-seconds a month.
    /// Typed, because no record Azure returns for the app or the plan states the grant.
    /// </summary>
    public const double FreeGrantGbSeconds = 100_000;

    /// <summary>Azure Functions pricing page, Flex Consumption on-demand free grant, per subscription: executions a month.</summary>
    public const double FreeGrantExecutions = 250_000;
}
// #endregion cloud-thresholds
