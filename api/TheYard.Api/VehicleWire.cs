using System.ComponentModel;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// The vehicle as the wire carries it: the dataset's twenty-nine fields, by
/// the dataset's names, and then the five facts the server derives so the
/// browser never has to (ADR: The API describes itself). Until 1.0.0.137 this
/// was a JSON node with five keys appended, which the wire did not mind and
/// the document could only describe as an object; the names, the values and
/// the order are the same as they were.
/// </summary>
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
    [property: Description("null means no reserve.")] int? ReservePrice,
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
    [property: Description("True once anybody has bought the vehicle outright; a sold vehicle takes no more bids from anyone.")] bool Sold);

/// <summary>
/// The wire shape of a vehicle: the dataset fields plus the server-derived
/// auction facts (window, status, minimum next bid, and whether it is sold).
/// Deriving these once, server-side, keeps the client from re-implementing
/// schedule math. The browser only formats and counts down.
/// </summary>
public static class VehicleWire
{
    // #region sold
    /// <summary>
    /// <paramref name="sold"/> is not a default parameter on purpose: every
    /// caller has the bid service in hand and has to say, because a listing
    /// that forgot would show a bought vehicle as open to everybody but its
    /// buyer, which is what every listing did until 1.0.0.110 (ADR: Accounts
    /// and per-user bids, the addendum on the second buyer).
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
            vehicle.ReservePrice,
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
            sold);
    }
    // #endregion sold
}
