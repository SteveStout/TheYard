// The resource cards after the plan, as definitions the one recorder and the one reader walk
// (ADR: One frame for every resource): each card names its resource, the readings it keeps, how
// it reads its minute and its hour, and how it draws itself in the shared frame. Adding a
// resource is a definition, not a new recorder, keep or route.

namespace TheYard.Api.Features.Admin.Cloud;

// #region resource-card
/// <summary>What one card is given to draw itself from: the window, the kept minutes and hours, and its line of the bill.</summary>
/// <param name="Window">The window asked for: 24h, 7d or 30d.</param>
/// <param name="Length">How long the window is.</param>
/// <param name="Now">When the card is drawn.</param>
/// <param name="Availability">Whether the keep is there, and one sentence about it.</param>
/// <param name="LastHour">The newest sixty kept minutes, oldest first.</param>
/// <param name="Buckets">The window's buckets.</param>
/// <param name="Month">The last thirty days at five minutes, for the dials' 95th and peak.</param>
/// <param name="Hours">The hours kept in the window, oldest first.</param>
/// <param name="CostMonth">The resource's month to date from the bill; null when the bill does not name it yet.</param>
/// <param name="RecordingSite">The configured name of the site whose process records, which the empty-keep sentence names.</param>
/// <param name="CostLines">Each further name the card's bill lines are matched on, with its month to date; null for a card with one line, which <paramref name="CostMonth"/> carries.</param>
public sealed record ResourceCardInputs(
    string Window,
    TimeSpan Length,
    DateTimeOffset Now,
    PlanHistoryAvailability Availability,
    IReadOnlyList<ResourceMinute> LastHour,
    IReadOnlyList<ResourceBucket> Buckets,
    IReadOnlyList<ResourceBucket> Month,
    IReadOnlyList<ResourceHour> Hours,
    double? CostMonth,
    string RecordingSite = "the recording site",
    IReadOnlyDictionary<string, double?>? CostLines = null);

/// <summary>One card under Cloud infrastructure, as the recorder and the reader see it.</summary>
/// <param name="Key">The card's route and container key: sqldb, cosmosdb, registry, insights, share, blob, mail, edge, nightly, claude or campaign.</param>
/// <param name="Container">The document store container its minutes are kept in.</param>
/// <param name="ResourceName">The resource's name on the bill, which its cost line is matched against.</param>
/// <param name="Readings">The names of the readings it keeps.</param>
/// <param name="Minutes">How it reads the minutes from one moment to another.</param>
/// <param name="Hour">How it reads an hour's slower facts.</param>
/// <param name="View">How it draws itself in the shared frame.</param>
/// <param name="Hourly">True when the source moves no faster than an hour, so it is read once an hour and kept as one document.</param>
/// <param name="SpecSource">Where the card's spec line is read.</param>
/// <param name="CostNames">Every name the bill is matched on when the card's resource is more than one line of it (a Function, its plan and its storage); null for one line, <paramref name="ResourceName"/>.</param>
/// <param name="KindPrefix">What the card's kept kinds open with when it shares another card's container; empty when the container is its own.</param>
public sealed record ResourceCard(
    string Key,
    string Container,
    string ResourceName,
    IReadOnlyList<string> Readings,
    Func<DateTimeOffset, DateTimeOffset, string, CancellationToken, Task<IReadOnlyList<ResourceMinute>>> Minutes,
    Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<ResourceHour>> Hour,
    Func<ResourceCardInputs, CloudReport> View,
    bool Hourly = false,
    string SpecSource = "ARM record",
    IReadOnlyList<string>? CostNames = null,
    string KindPrefix = "")
{
    /// <summary>How old the newest kept reading may be before a read asks the source again.</summary>
    public TimeSpan Stale => Hourly ? TimeSpan.FromMinutes(61) : PlanHistoryReader.Fresh + TimeSpan.FromMinutes(1);
}
// #endregion resource-card
