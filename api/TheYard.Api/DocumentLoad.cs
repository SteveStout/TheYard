// The document store's side of the Machines card on the Admin tab: the operations ring folded into
// request units a minute. It is its own file because the document store has no machine reading to
// give, so its side of the card is built from a different ring by different arithmetic. The rest
// of the card starts in Machines.cs.
using TheYard.Application;

namespace TheYard.Api;

// #region document-load
/// <summary>
/// The document store has no memory reading to give: Azure Cosmos DB is sold
/// by request unit and says nothing about the machine underneath, which is the
/// honest difference between the two stores on this card. What it does say is
/// what each operation cost, and this folds the operations ring into request
/// units a minute against the thousand a second the free tier allows.
/// </summary>
public static class DocumentLoad
{
    /// <summary>The free tier's allowance, which every number here is a share of.</summary>
    public const int FreeRequestUnitsPerSecond = 1000;

    /// <summary>The operations ring as the card's document side: totals, durations, a minute at a time, and how far back it reaches; or a note when nothing has been sent yet.</summary>
    public static DocumentLoadView From(IReadOnlyList<StoreOperation> operations, string store, DateTimeOffset now)
    {
        if (operations.Count == 0)
        {
            return new DocumentLoadView(store, false, "nothing has been sent to the document store in this container yet", 0, 0, null, null, [], null);
        }

        var minutes = operations
            .GroupBy(operation => new DateTimeOffset(operation.At.Year, operation.At.Month, operation.At.Day, operation.At.Hour, operation.At.Minute, 0, TimeSpan.Zero))
            .OrderBy(group => group.Key)
            .Select(group => new DocumentMinute(
                group.Key,
                Math.Round(group.Sum(operation => operation.RequestCharge), 2),
                group.Count(),
                // The minute's charge spread over its sixty seconds, as a share
                // of the thousand request units a second the free tier allows:
                // the average rate the minute ran at against an allowance that
                // is itself a rate. A burst inside the minute runs higher than
                // this for its few seconds, and the store says nothing finer.
                Math.Round(group.Sum(operation => operation.RequestCharge) / 60 / FreeRequestUnitsPerSecond * 100, 3)))
            .ToList();

        long[] durations = operations.Select(operation => operation.DurationMs).ToArray();
        // How far back the ring reaches, in whole minutes up to now: the ring
        // holds a number of operations and not a stretch of time, so a total
        // over it means nothing until it says what stretch it covers.
        var oldest = operations.Min(operation => operation.At);
        int spanMinutes = Math.Max(1, (int)Math.Ceiling((now - oldest).TotalMinutes));
        return new DocumentLoadView(
            store,
            true,
            null,
            Math.Round(operations.Sum(operation => operation.RequestCharge), 2),
            operations.Count,
            Percentiles.Of(durations, 50),
            Percentiles.Of(durations, 95),
            minutes,
            spanMinutes);
    }
}

/// <summary>One minute of the operations ring: what it cost, how many operations, and its average rate as a share of the free allowance.</summary>
/// <param name="At">The start of the minute, UTC.</param>
/// <param name="RequestUnits">The request units spent in the minute, rounded to two places.</param>
/// <param name="Operations">How many operations ran in the minute.</param>
/// <param name="ShareOfFreePercent">The minute's average request units a second, its charge over sixty, as a percentage of the free 1,000 a second.</param>
public sealed record DocumentMinute(DateTimeOffset At, double RequestUnits, int Operations, double ShareOfFreePercent);

/// <summary>The document store's side of the card.</summary>
/// <param name="Store">The key of the document store.</param>
/// <param name="Available">True when this container has sent the document store anything yet.</param>
/// <param name="Note">Why there is no reading, or null when there is one.</param>
/// <param name="RequestUnits">The request units spent across the operations ring, rounded to two places.</param>
/// <param name="Operations">How many operations the ring holds.</param>
/// <param name="P50Ms">The median operation duration, in milliseconds; null when there is no reading.</param>
/// <param name="P95Ms">The 95th percentile operation duration, in milliseconds; null when there is no reading.</param>
/// <param name="Minutes">The ring folded a minute at a time.</param>
/// <param name="SpanMinutes">How many minutes back from now the ring reaches, rounded up; null when it holds nothing.</param>
public sealed record DocumentLoadView(
    string Store,
    bool Available,
    string? Note,
    double RequestUnits,
    int Operations,
    long? P50Ms,
    long? P95Ms,
    IReadOnlyList<DocumentMinute> Minutes,
    int? SpanMinutes);
// #endregion document-load
