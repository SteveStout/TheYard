// The review route's short path, stop 1 (ADR: The review route gives a reason before it asks, the addendum on the
// short path): the chart at the top of the Quality page at tile size, async methods with no CancellationToken by
// version, read through the same reader that fills that page's fence, so the tile and the page cannot disagree.
using TheYard.Application.Shared;
using TheYard.Application.Shared.History;

namespace TheYard.Application.Features.Review;

// #region review-trend
/// <summary>One quality number's readings, for the short path's chart.</summary>
/// <param name="Number">The number's key in the history.</param>
/// <param name="Readings">One line a reading, "date | version | value | source", oldest first, as the page's fence holds them.</param>
public sealed record ReviewTrend(string Number, IReadOnlyList<string> Readings);

/// <summary>The short path's chart: the number at the top of the Quality page.</summary>
public sealed record ReadReviewTrendQuery;

/// <summary>Reads the number the Quality page opens on, through the port the Docs slice answers.</summary>
/// <param name="trend">The quality history's readings.</param>
public sealed class ReadReviewTrendHandler(IQualityTrend trend) : IQueryHandler<ReadReviewTrendQuery, ReviewTrend>
{
    /// <summary>The number the Quality page charts first, and the short path with it.</summary>
    public const string Number = "async_without_cancellation";

    /// <summary>The readings of <see cref="Number"/>.</summary>
    /// <param name="query">Nothing to ask.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ReviewTrend> HandleAsync(ReadReviewTrendQuery query, CancellationToken cancellation) =>
        new(Number, await trend.ReadingsAsync(Number, cancellation));
}
// #endregion review-trend
