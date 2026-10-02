// Copies a vehicle between its database row and its domain record, both ways. It has a file of
// its own because it is the one place a field can be dropped on the way in or out of the
// database, and a reader checking that every field is copied should find nothing else here.
using TheYard.Data;

namespace TheYard.Infrastructure;

/// <summary>
/// Row to domain and back. The mapping is dull on purpose and lives in one
/// place, because the interesting failure mode of a persistence layer is a
/// field that stops being copied without anybody noticing.
/// </summary>
public static class VehicleRows
{
    // #region mapping
    /// <summary>The domain record for a stored row, every field copied across.</summary>
    public static Vehicle ToVehicle(this VehicleRow row) => new()
    {
        Id = row.Id,
        Vin = row.Vin,
        Year = row.Year,
        Make = row.Make,
        Model = row.Model,
        Trim = row.Trim,
        BodyStyle = row.BodyStyle,
        ExteriorColor = row.ExteriorColor,
        InteriorColor = row.InteriorColor,
        Engine = row.Engine,
        Transmission = row.Transmission,
        Drivetrain = row.Drivetrain,
        OdometerKm = row.OdometerKm,
        FuelType = row.FuelType,
        ConditionGrade = row.ConditionGrade,
        ConditionReport = row.ConditionReport,
        DamageNotes = row.DamageNotes,
        TitleStatus = row.TitleStatus,
        Province = row.Province,
        City = row.City,
        AuctionStart = row.AuctionStart,
        StartingBid = row.StartingBid,
        ReservePrice = row.ReservePrice,
        BuyNowPrice = row.BuyNowPrice,
        Images = row.Images,
        SellingDealership = row.SellingDealership,
        Lot = row.Lot,
        CurrentBid = row.CurrentBid,
        BidCount = row.BidCount,
    };

    /// <summary>
    /// The row for a domain record, every field copied across, with its seed
    /// position as Seq so the catalogue reads back in the order it was written.
    /// </summary>
    public static VehicleRow ToRow(this Vehicle vehicle, int seq) => new()
    {
        Seq = seq,
        Id = vehicle.Id,
        Vin = vehicle.Vin,
        Year = vehicle.Year,
        Make = vehicle.Make,
        Model = vehicle.Model,
        Trim = vehicle.Trim,
        BodyStyle = vehicle.BodyStyle,
        ExteriorColor = vehicle.ExteriorColor,
        InteriorColor = vehicle.InteriorColor,
        Engine = vehicle.Engine,
        Transmission = vehicle.Transmission,
        Drivetrain = vehicle.Drivetrain,
        OdometerKm = vehicle.OdometerKm,
        FuelType = vehicle.FuelType,
        ConditionGrade = vehicle.ConditionGrade,
        ConditionReport = vehicle.ConditionReport,
        DamageNotes = [.. vehicle.DamageNotes],
        TitleStatus = vehicle.TitleStatus,
        Province = vehicle.Province,
        City = vehicle.City,
        AuctionStart = vehicle.AuctionStart,
        StartingBid = vehicle.StartingBid,
        ReservePrice = vehicle.ReservePrice,
        BuyNowPrice = vehicle.BuyNowPrice,
        Images = [.. vehicle.Images],
        SellingDealership = vehicle.SellingDealership,
        Lot = vehicle.Lot,
        CurrentBid = vehicle.CurrentBid,
        BidCount = vehicle.BidCount,
    };
    // #endregion mapping
}
