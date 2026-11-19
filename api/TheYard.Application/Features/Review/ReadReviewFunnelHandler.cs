// The Admin card's question (ADR: Ten minutes, seven stops, the addenda on the reach rows and the second
// actions): of the visitors who opened the review route in a window, how many reached each stop, how many
// reached a second action and how many the running system or the booking button, on each first screen too,
// and how far each tagged code got. The slice's one query, over the reading side only.
using System.Globalization;
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Review;

// #region read-review-funnel
/// <summary>The funnel for a window: the kept rows folded to one a visitor, then counted stop by stop and by code.</summary>
/// <param name="reader">The kept reach, reading side.</param>
/// <param name="clock">The server's clock; the window is measured back from now.</param>
public sealed class ReadReviewFunnelHandler(IReviewReader reader, TimeProvider clock) : IQueryHandler<ReadReviewFunnelQuery, ReviewFunnel>
{
    /// <summary>How many places the funnel has: opened, then the seven stops.</summary>
    public const int Places = ReviewFolding.LastStop + 1;

    /// <summary>The funnel; a reader that cannot be read answers an empty one that says so.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ReviewFunnel> HandleAsync(ReadReviewFunnelQuery query, CancellationToken cancellation)
    {
        string name = ReviewWindows.NameOf(query.Window);
        if (!await reader.AvailableAsync(cancellation))
        {
            return new ReviewFunnel(false, name, 0, [.. Enumerable.Repeat(0, Places)], [], null, reader.Source, new ReviewSecond(0, 0, ReviewActions.CountedFrom, [.. ReviewActions.Screens.Select(screen => new ReviewScreenRow(screen, 0, 0, 0))]));
        }
        var since = clock.GetUtcNow() - ReviewWindows.LengthOf(query.Window);
        var rows = (await reader.RowsSinceAsync(since.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellation))
            .Where(row => row.Last >= since)
            .ToList();
        // A visitor on two days is one visitor: the days fold to the highest stop, the earliest first, the latest
        // last and every action seen on any of them.
        var visitors = rows
            .GroupBy(row => row.Hash, StringComparer.Ordinal)
            .Select(days => days.OrderBy(row => row.First).Aggregate(ReviewFolding.Merge))
            .ToList();
        int[] reached = [.. Enumerable.Range(0, Places).Select(stop => visitors.Count(visitor => visitor.Stop >= stop))];
        var byCode = rows
            .Where(row => row.Code is not null)
            .GroupBy(row => row.Code!, StringComparer.Ordinal)
            .Select(code => new ReviewCodeRow(
                code.Key,
                code.Max(row => row.Stop),
                (int)Math.Round((code.Max(row => row.Last) - code.Min(row => row.First)).TotalMinutes),
                code.Max(row => row.Day)!))
            .OrderByDescending(row => row.LastDay, StringComparer.Ordinal)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ToList();
        var second = new ReviewSecond(
            visitors.Count(ReviewActions.ReachedASecond),
            visitors.Count(ReviewActions.ReachedRunningOrBook),
            ReviewActions.CountedFrom,
            [.. ReviewActions.Screens.Select(screen => ScreenRow(screen, visitors))]);
        return new ReviewFunnel(true, name, visitors.Count, reached, byCode, await reader.FirstDayAsync(cancellation), reader.Source, second);
    }

    /// <summary>One first screen's row: the visitors whose first screen it was, and the two figures among them.</summary>
    /// <param name="screen">"verdict" or "mirror".</param>
    /// <param name="visitors">Every visitor in the window, folded to one row each.</param>
    private static ReviewScreenRow ScreenRow(string screen, IReadOnlyList<ReviewRow> visitors)
    {
        var on = visitors.Where(visitor => ReviewActions.ScreenOf(visitor) == screen).ToList();
        return new ReviewScreenRow(screen, on.Count, on.Count(ReviewActions.ReachedASecond), on.Count(ReviewActions.ReachedRunningOrBook));
    }
}

/// <summary>The card's funnel for one window.</summary>
/// <param name="Window">24h, 7d, 30d or 90d.</param>
public sealed record ReadReviewFunnelQuery(ReviewWindow Window);
// #endregion read-review-funnel
