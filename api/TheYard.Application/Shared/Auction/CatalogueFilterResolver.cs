// A visitor's filter resolved to the store's own spelling. A class of its own because it is the edge
// between a request's words and the store's, and Stock with it was over three hundred lines (ADR-088).
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>A <see cref="VehicleFilter"/> in the catalogue port's terms, for <see cref="Stock"/>.</summary>
public static class CatalogueFilterResolver
{
    // #region resolve
    /// <summary>
    /// The visitor's filter in the store's terms: each field value resolved to the store's own
    /// spelling against the facets, ignoring case as the filter always has, so the stores compare
    /// with plain equality. A value no vehicle has resolves to nothing, and the answer is the
    /// empty page without asking the store.
    /// </summary>
    internal static CatalogueFilter? Resolve(VehicleFilter filter, AuctionClock clock, InventoryFacets facets)
    {
        if (!TryCanon(filter.Make, facets.Makes, out var make) ||
            !TryCanon(filter.BodyStyle, facets.BodyStyles, out var bodyStyle) ||
            !TryCanon(filter.TitleStatus, facets.TitleStatuses, out var titleStatus) ||
            !TryCanon(filter.Province, facets.Provinces, out var province))
        {
            return null;
        }

        string[] words = string.IsNullOrWhiteSpace(filter.Query)
            ? []
            : filter.Query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new CatalogueFilter
        {
            SinceAnchorMs = clock.NowMs - clock.AnchorMs,
            Make = make,
            BodyStyle = bodyStyle,
            TitleStatus = titleStatus,
            Province = province,
            Segment = filter.Status switch
            {
                AuctionStatus.Live => CatalogueSegment.Live,
                AuctionStatus.Upcoming => CatalogueSegment.Upcoming,
                AuctionStatus.Ended => CatalogueSegment.Ended,
                _ => CatalogueSegment.Any,
            },
            MinCondition = filter.MinCondition,
            PriceMin = filter.PriceMin,
            PriceMax = filter.PriceMax,
            Tokens = [.. words.Select(word => new CatalogueToken(word, StatusWordsHolding(word)))],
        };
    }

    private static readonly (string Word, CatalogueSegment Segment)[] StatusWords =
    [
        (AuctionStatus.Live.ToString().ToLowerInvariant(), CatalogueSegment.Live),
        (AuctionStatus.Upcoming.ToString().ToLowerInvariant(), CatalogueSegment.Upcoming),
        (AuctionStatus.Ended.ToString().ToLowerInvariant(), CatalogueSegment.Ended),
    ];

    private static CatalogueSegment[] StatusWordsHolding(string word) =>
        [.. StatusWords.Where(status => VehicleSearchIndex.Holds(status.Word, word)).Select(status => status.Segment)];

    private static bool TryCanon(string? wanted, IReadOnlyList<string> known, out string? canon)
    {
        canon = null;
        if (wanted is null)
        {
            return true;
        }
        canon = known.FirstOrDefault(value => value.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        return canon is not null;
    }
    // #endregion resolve
}
