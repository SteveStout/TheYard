// The one rule the review route's reach is kept by (ADR: Ten minutes, seven stops, the addendum on the
// reach rows), pure, so the collector, the memory keeper and the document store cannot fold one report two
// ways: a higher stop replaces a lower one and never the reverse, the first time and the first code stay,
// and the last time moves.
namespace TheYard.Application.Features.Review;

// #region review-folding
/// <summary>Reports folded to a row a visitor a day, and a row folded into what is kept.</summary>
public static class ReviewFolding
{
    /// <summary>The last stop on the route; a report past it, or below opened, is not one.</summary>
    public const int LastStop = 7;

    /// <summary>Whether a number is a stop a report may carry: 0 for opened, 1 to 7.</summary>
    /// <param name="stop">The number reported.</param>
    public static bool IsStop(int stop) => stop is >= 0 and <= LastStop;

    /// <summary>A batch of reports as one row a visitor a day, oldest day first.</summary>
    /// <param name="reaches">The batch.</param>
    public static IReadOnlyList<ReviewRow> Fold(IReadOnlyList<ReviewReach> reaches) =>
        [.. reaches
            .Where(reach => IsStop(reach.Stop))
            .GroupBy(reach => (reach.Day, reach.Hash))
            .Select(day => day.OrderBy(reach => reach.At).Select(Row).Aggregate(Merge))
            .OrderBy(row => row.Day, StringComparer.Ordinal)
            .ThenBy(row => row.Hash, StringComparer.Ordinal)];

    /// <summary>One report as a row of its own.</summary>
    /// <param name="reach">The report.</param>
    public static ReviewRow Row(ReviewReach reach) => new(reach.Hash, reach.Day, reach.Stop, reach.Code, reach.At, reach.At);

    /// <summary>
    /// A row folded into the one kept for the same visitor and day: the higher stop, the first code
    /// (a code is kept once per visitor and day), the earlier first and the later last.
    /// </summary>
    /// <param name="kept">The row as kept.</param>
    /// <param name="added">The row the batch adds.</param>
    public static ReviewRow Merge(ReviewRow kept, ReviewRow added) => kept with
    {
        Stop = Math.Max(kept.Stop, added.Stop),
        Code = kept.Code ?? added.Code,
        First = added.First < kept.First ? added.First : kept.First,
        Last = added.Last > kept.Last ? added.Last : kept.Last,
    };
}
// #endregion review-folding
