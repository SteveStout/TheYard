using System.ComponentModel;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// The vehicle as the wire carries it: the dataset's fields, by the dataset's
/// names, and then the six facts the server derives so the browser never has
/// to (ADR: The API describes itself). The one dataset field it leaves out is
/// the reserve price: the site shows whether the reserve is met and never the
/// amount, so the amount stays on the server and the wire carries the state.
/// </summary>
/// <param name="Id">The vehicle's id, a GUID.</param>
/// <param name="Vin">The vehicle identification number.</param>
/// <param name="Year">The model year.</param>
/// <param name="Make">The manufacturer, such as Mazda.</param>
/// <param name="Model">The model, such as CX-5.</param>
/// <param name="Trim">The trim level.</param>
/// <param name="BodyStyle">The body style, such as SUV.</param>
/// <param name="ExteriorColor">The exterior color.</param>
/// <param name="InteriorColor">The interior color.</param>
/// <param name="Engine">The engine, such as 2.5L I4.</param>
/// <param name="Transmission">The transmission, such as automatic.</param>
/// <param name="Drivetrain">The drivetrain, such as FWD.</param>
/// <param name="OdometerKm">The odometer reading, in kilometres.</param>
/// <param name="FuelType">The fuel type, such as gasoline.</param>
/// <param name="ConditionGrade">The condition grade the dataset gives it; higher is better.</param>
/// <param name="ConditionReport">The inspector's written condition report.</param>
/// <param name="DamageNotes">Notes on any damage; empty when there is none.</param>
/// <param name="TitleStatus">The title status, such as clean.</param>
/// <param name="Province">The province the vehicle is in.</param>
/// <param name="City">The city the vehicle is in.</param>
/// <param name="AuctionStart">The dataset's own date string, passed through; nothing is derived from it.</param>
/// <param name="StartingBid">The opening bid, in whole dollars.</param>
/// <param name="BuyNowPrice">The buy-now price in whole dollars; null means no buy-now option.</param>
/// <param name="Images">Gallery paths under /api/images, chosen for the body style.</param>
/// <param name="SellingDealership">The dealership selling the vehicle.</param>
/// <param name="Lot">The lot number, such as A-0001.</param>
/// <param name="CurrentBid">The standing price, or null until the first bid.</param>
/// <param name="BidCount">How many bids the vehicle has had.</param>
/// <param name="AuctionStartsAt">When the auction opens, in milliseconds since the epoch, UTC. Derived from the id on the server's clock.</param>
/// <param name="AuctionEndsAt">When the auction closes, in milliseconds since the epoch, UTC.</param>
/// <param name="AuctionStatus">live, upcoming or ended, on the server's clock at the moment of the response.</param>
/// <param name="MinNextBid">The smallest bid the rules will accept next, in whole dollars.</param>
/// <param name="ReserveState">no-reserve, met or not-met: whether the standing bid has reached the seller's reserve. The reserve amount is never sent.</param>
/// <param name="Sold">True once anybody has bought the vehicle outright; a sold vehicle takes no more bids from anyone.</param>
public sealed record VehicleView(
    string Id,
    string Vin,
    int Year,
    string Make,
    string Model,
    string Trim,
    string BodyStyle,
    string ExteriorColor,
    string InteriorColor,
    string Engine,
    string Transmission,
    string Drivetrain,
    int OdometerKm,
    string FuelType,
    double ConditionGrade,
    string ConditionReport,
    IReadOnlyList<string> DamageNotes,
    string TitleStatus,
    string Province,
    string City,
    [property: Description("The dataset's own date string, passed through; nothing is derived from it.")] string AuctionStart,
    int StartingBid,
    [property: Description("null means no buy-now option.")] int? BuyNowPrice,
    [property: Description("Gallery paths under /api/images, chosen for the body style.")] IReadOnlyList<string> Images,
    string SellingDealership,
    string Lot,
    [property: Description("The standing price, or null until the first bid.")] int? CurrentBid,
    int BidCount,
    [property: Description("When the auction opens, in milliseconds since the epoch, UTC. Derived from the id on the server's clock.")] long AuctionStartsAt,
    [property: Description("When the auction closes, in milliseconds since the epoch, UTC.")] long AuctionEndsAt,
    [property: Description("live, upcoming or ended, on the server's clock at the moment of the response.")] string AuctionStatus,
    [property: Description("The smallest bid the rules will accept next, in whole dollars.")] int MinNextBid,
    [property: Description("no-reserve, met or not-met: whether the standing bid has reached the seller's reserve. The reserve amount is never sent.")] string ReserveState,
    [property: Description("True once anybody has bought the vehicle outright; a sold vehicle takes no more bids from anyone.")] bool Sold);

/// <summary>
/// The wire shape of a vehicle: the dataset fields plus the server-derived
/// auction facts (window, status, minimum next bid, reserve state, and whether
/// it is sold). Deriving these once, server-side, keeps the client from
/// re-implementing schedule math and keeps the reserve amount off the wire.
/// The browser only formats and counts down.
/// </summary>
public static class VehicleWire
{
    // #region reserve-state
    /// <summary>The vehicle has no reserve, so it sells at any price.</summary>
    public const string NoReserve = "no-reserve";

    /// <summary>The standing bid is at or above the reserve.</summary>
    public const string ReserveMet = "met";

    /// <summary>The standing bid is below the reserve, or nobody has bid yet.</summary>
    public const string ReserveNotMet = "not-met";

    /// <summary>
    /// The wire's word for whether the reserve is met. The rule itself is the domain's
    /// (StandingRules, read as vehicle.Reserve); this only spells its answer the way the
    /// browser reads it.
    /// </summary>
    public static string ReserveState(ReserveStatus status) =>
        status switch
        {
            ReserveStatus.NoReserve => NoReserve,
            ReserveStatus.Met => ReserveMet,
            _ => ReserveNotMet,
        };
    // #endregion reserve-state

    // #region sold
    /// <summary>
    /// <paramref name="sold"/> is not a default parameter on purpose: every
    /// caller has the auction in hand and has to say, because a listing that
    /// forgot would show a bought vehicle as open to everybody but its buyer.
    /// </summary>
    public static VehicleView ToWire(Vehicle vehicle, AuctionClock clock, bool sold)
    {
        var window = AuctionSchedule.Window(vehicle.Id, clock.AnchorMs);
        return new VehicleView(
            vehicle.Id,
            vehicle.Vin,
            vehicle.Year,
            vehicle.Make,
            vehicle.Model,
            vehicle.Trim,
            vehicle.BodyStyle,
            vehicle.ExteriorColor,
            vehicle.InteriorColor,
            vehicle.Engine,
            vehicle.Transmission,
            vehicle.Drivetrain,
            vehicle.OdometerKm,
            vehicle.FuelType,
            vehicle.ConditionGrade,
            vehicle.ConditionReport,
            vehicle.DamageNotes,
            vehicle.TitleStatus,
            vehicle.Province,
            vehicle.City,
            vehicle.AuctionStart,
            vehicle.StartingBid,
            vehicle.BuyNowPrice,
            vehicle.Images,
            vehicle.SellingDealership,
            vehicle.Lot,
            vehicle.CurrentBid,
            vehicle.BidCount,
            window.StartsAtMs,
            window.EndsAtMs,
            // The status stays the clock's, because the browser recomputes it
            // from the window as time passes and would overwrite a status that
            // said otherwise. Sold rides beside it as its own fact.
            AuctionSchedule.Status(window, clock.NowMs).ToString().ToLowerInvariant(),
            BidRules.MinNextBid(vehicle),
            // From the vehicle after the overlays, so the state matches the
            // standing bid this same answer carries.
            ReserveState(vehicle.Reserve),
            sold);
    }
    // #endregion sold
}
