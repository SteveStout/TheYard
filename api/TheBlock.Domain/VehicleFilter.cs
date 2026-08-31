using TheBlock.Data;

namespace TheBlock.Domain;

/// <summary>
/// Inventory search criteria. Null fields mean "any". Price bounds apply to
/// the price a buyer competes against — the high bid, or the opening ask when
/// no bids exist. The auction-status criterion is evaluated against windows
/// derived from the supplied <see cref="AuctionClock"/>.
/// </summary>
public sealed record VehicleFilter
{
    /// <summary>
    /// Free text; every whitespace-separated token must match somewhere in the
    /// vehicle's searchable fields — identity (year, make, model, trim) plus
    /// every field the filters cover (body style, title status, province, and
    /// the derived auction status: live/upcoming/ended) and the city.
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

    public bool Matches(Vehicle vehicle, AuctionClock clock)
    {
        if (!MatchesQuery(vehicle, clock))
        {
            return false;
        }
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

    private bool MatchesQuery(Vehicle vehicle, AuctionClock clock)
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return true;
        }
        string haystack =
            ($"{vehicle.Year} {vehicle.Make} {vehicle.Model} {vehicle.Trim} " +
             $"{vehicle.BodyStyle} {vehicle.TitleStatus} {vehicle.Province} {vehicle.City} " +
             $"{AuctionSchedule.StatusFor(vehicle.Id, clock)}")
            .ToLowerInvariant();
        return Query
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(haystack.Contains);
    }
}
