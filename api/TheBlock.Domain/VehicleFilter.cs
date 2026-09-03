using TheBlock.Data;

namespace TheBlock.Domain;

/// <summary>
/// Inventory search criteria. Null fields mean "any". Price bounds apply to
/// the price a buyer competes against, which is the high bid, or the opening
/// ask when no bids exist. The auction-status criterion is evaluated against
/// windows derived from the supplied <see cref="AuctionClock"/>.
/// </summary>
public sealed record VehicleFilter
{
    /// <summary>
    /// Free text; every whitespace-separated token must match somewhere in the
    /// vehicle's searchable fields, which are identity (year, make, model,
    /// trim) plus every field the filters cover (body style, title status,
    /// province, and the derived auction status: live/upcoming/ended) and the
    /// city.
    /// </summary>
    public string? Query { get; init; }

    public string? Make { get; init; }
    public string? BodyStyle { get; init; }
    public string? TitleStatus { get; init; }
    public string? Province { get; init; }
    public AuctionStatus? Status { get; init; }
    public double? MinCondition { get; init; }
    public int? PriceMin { get; init; }
    public int? PriceMax { get; init; }

    // #region compile
    /// <summary>
    /// Turns this filter into a predicate, doing once whatever does not depend
    /// on the vehicle. That is the query's tokens: lowercasing and splitting
    /// them inside the loop meant repeating the same two allocations for every
    /// row scanned, which on the synthetic 100,000-row dataset is 100,000
    /// copies of a string the user typed once (ADR: Search index).
    ///
    /// <paramref name="index"/> is optional so that every existing caller and
    /// test still reads <c>filter.Matches(vehicle, clock)</c>. Without it the
    /// searchable text is computed per vehicle, exactly as before.
    /// </summary>
    public Func<Vehicle, bool> Compile(AuctionClock clock, VehicleSearchIndex? index = null)
    {
        string[] tokens = string.IsNullOrWhiteSpace(Query)
            ? []
            : Query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return vehicle => MatchesQuery(vehicle, clock, tokens, index) && MatchesFields(vehicle, clock);
    }
    // #endregion compile

    /// <summary>
    /// One vehicle against this filter. Kept for callers with a single vehicle
    /// in hand; a scan should <see cref="Compile"/> once and reuse the result.
    /// </summary>
    public bool Matches(Vehicle vehicle, AuctionClock clock) => Compile(clock)(vehicle);

    private bool MatchesFields(Vehicle vehicle, AuctionClock clock)
    {
        if (!MatchesExactly(Make, vehicle.Make) ||
            !MatchesExactly(BodyStyle, vehicle.BodyStyle) ||
            !MatchesExactly(TitleStatus, vehicle.TitleStatus) ||
            !MatchesExactly(Province, vehicle.Province))
        {
            return false;
        }
        if (Status is { } status && AuctionSchedule.StatusFor(vehicle.Id, clock) != status)
        {
            return false;
        }
        if (MinCondition is { } minCondition && vehicle.ConditionGrade < minCondition)
        {
            return false;
        }

        int price = vehicle.CurrentBid ?? vehicle.StartingBid;
        if (PriceMin is { } priceMin && price < priceMin)
        {
            return false;
        }
        if (PriceMax is { } priceMax && price > priceMax)
        {
            return false;
        }
        return true;
    }

    private static bool MatchesExactly(string? wanted, string actual) =>
        wanted is null || actual.Equals(wanted, StringComparison.OrdinalIgnoreCase);

    // #region query
    /// <summary>
    /// Every token must appear somewhere in the vehicle's searchable text. The
    /// text splits in two: the part that never changes, which the index holds,
    /// and the auction status, which the clock decides. A token that the
    /// static part already satisfies never triggers the status computation, so
    /// the common query pays for the hash and the date math zero times instead
    /// of once per row.
    ///
    /// This is the same answer the single concatenated haystack gave, because
    /// tokens are split on whitespace and so can never straddle the space
    /// between the two parts.
    /// </summary>
    private static bool MatchesQuery(
        Vehicle vehicle, AuctionClock clock, string[] tokens, VehicleSearchIndex? index)
    {
        if (tokens.Length == 0)
        {
            return true;
        }
        string text = index is null ? VehicleSearchIndex.TextFor(vehicle) : index.For(vehicle);
        string? status = null;
        foreach (string token in tokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
            {
                continue;
            }
            status ??= AuctionSchedule.StatusFor(vehicle.Id, clock).ToString().ToLowerInvariant();
            if (!status.Contains(token, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
    // #endregion query
}
