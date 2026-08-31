using TheBlock.Data;

namespace TheBlock.Domain;

public enum VehicleSort
{
    EndingSoonest,
    PriceAsc,
    PriceDesc,
    Condition,
    MostBids,
}

/// <summary>
/// Result ordering. EndingSoonest mirrors the ranking the frontend used to
/// compute client-side: live auctions first (closest to ending), then
/// upcoming (starting soonest), then ended (most recently ended).
/// </summary>
public static class VehicleOrdering
{
    private const long UpcomingBand = 1_000_000_000_000_000;
    private const long EndedBand = 2_000_000_000_000_000;

    public static IEnumerable<Vehicle> Sort(
        IEnumerable<Vehicle> vehicles,
        VehicleSort sort,
        AuctionClock clock) => sort switch
    {
        VehicleSort.EndingSoonest => vehicles.OrderBy(v => EndingSoonestRank(v, clock)),
        VehicleSort.PriceAsc => vehicles.OrderBy(CompetingPrice),
        VehicleSort.PriceDesc => vehicles.OrderByDescending(CompetingPrice),
        VehicleSort.Condition => vehicles.OrderByDescending(v => v.ConditionGrade),
        VehicleSort.MostBids => vehicles.OrderByDescending(v => v.BidCount),
        _ => vehicles,
    };

    private static int CompetingPrice(Vehicle vehicle) => vehicle.CurrentBid ?? vehicle.StartingBid;

    private static long EndingSoonestRank(Vehicle vehicle, AuctionClock clock)
    {
        var window = AuctionSchedule.Window(vehicle.Id, clock.AnchorMs);
        return AuctionSchedule.Status(window, clock.NowMs) switch
        {
            AuctionStatus.Live => window.EndsAtMs,
            AuctionStatus.Upcoming => UpcomingBand + window.StartsAtMs,
            _ => EndedBand - window.EndsAtMs,
        };
    }
}
