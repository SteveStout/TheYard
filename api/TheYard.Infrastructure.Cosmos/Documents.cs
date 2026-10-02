// What the document store keeps, and the mapping between those documents and the domain
// records. This file holds the mapping; the shapes live one file per container, so what a
// container holds is one short file to open:
//   Containers.cs            the container names and their partition keys (region containers)
//   Documents.Vehicles.cs    VehicleDocument, the vehicles container (region documents)
//   Documents.Photos.cs      PhotoDocument, the photos container (region documents)
//   Documents.Bids.cs        BidDocument, the bids container (region documents)
//   Documents.Users.cs       UserDocument and EmailClaimDocument, the users container
//                            (region documents)
//   Documents.Activity.cs    ActivityHourDocument and ActivityVisitorDocument, the activity container
//                            (regions documents, activity-documents)
//   Documents.Logs.cs        LogDocument, the logs container (regions documents, log-documents)
//   Documents.Resets.cs      ResetLinkDocument, the resets container (region documents)
//   Documents.Machines.cs    MachineMinuteDocument, the machines container
//                            (regions documents, machine-documents)
//
// Every shape is snake case on the wire, like the dataset and the API, which is what makes a
// document readable beside the JSON it was seeded from. There is no mapper between these and
// the domain records except the dull kind below that copies fields one at a time, for the same
// reason VehicleRows exists on the relational side: the interesting failure of a persistence
// layer is a field that stops being copied without anything saying so.
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>Document to domain and back, field by field, in one place.</summary>
public static class Documents
{
    // #region mapping
    /// <summary>The domain vehicle a seed document holds. The images are left empty: the gallery derives them.</summary>
    public static Vehicle ToVehicle(this VehicleDocument d) => new()
    {
        Id = d.Id,
        Vin = d.Vin,
        Year = d.Year,
        Make = d.Make,
        Model = d.Model,
        Trim = d.Trim,
        BodyStyle = d.BodyStyle,
        ExteriorColor = d.ExteriorColor,
        InteriorColor = d.InteriorColor,
        Engine = d.Engine,
        Transmission = d.Transmission,
        Drivetrain = d.Drivetrain,
        OdometerKm = d.OdometerKm,
        FuelType = d.FuelType,
        ConditionGrade = d.ConditionGrade,
        ConditionReport = d.ConditionReport,
        DamageNotes = d.DamageNotes,
        TitleStatus = d.TitleStatus,
        Province = d.Province,
        City = d.City,
        AuctionStart = d.AuctionStart,
        StartingBid = d.StartingBid,
        ReservePrice = d.ReservePrice,
        BuyNowPrice = d.BuyNowPrice,
        // Not stored: the gallery derives them (see VehicleDocument).
        Images = [],
        SellingDealership = d.SellingDealership,
        Lot = d.Lot,
        CurrentBid = d.CurrentBid,
        BidCount = d.BidCount,
    };

    /// <summary>The document for a domain vehicle, with its place in the seed file so a read can restore the order.</summary>
    public static VehicleDocument ToDocument(this Vehicle v, int seq) => new()
    {
        Id = v.Id,
        Seq = seq,
        Vin = v.Vin,
        Year = v.Year,
        Make = v.Make,
        Model = v.Model,
        Trim = v.Trim,
        BodyStyle = v.BodyStyle,
        ExteriorColor = v.ExteriorColor,
        InteriorColor = v.InteriorColor,
        Engine = v.Engine,
        Transmission = v.Transmission,
        Drivetrain = v.Drivetrain,
        OdometerKm = v.OdometerKm,
        FuelType = v.FuelType,
        ConditionGrade = v.ConditionGrade,
        ConditionReport = v.ConditionReport,
        DamageNotes = [.. v.DamageNotes],
        TitleStatus = v.TitleStatus,
        Province = v.Province,
        City = v.City,
        AuctionStart = v.AuctionStart,
        StartingBid = v.StartingBid,
        ReservePrice = v.ReservePrice,
        BuyNowPrice = v.BuyNowPrice,
        SellingDealership = v.SellingDealership,
        Lot = v.Lot,
        CurrentBid = v.CurrentBid,
        BidCount = v.BidCount,
    };

    /// <summary>The photo manifest entry a document holds.</summary>
    public static PhotoEntry ToEntry(this PhotoDocument d) => new(d.Id, d.Style, d.Title);

    /// <summary>The document for a photo manifest entry, under its file name, with its place in the manifest.</summary>
    public static PhotoDocument ToDocument(this PhotoEntry p, int seq) => new() { Id = p.File, Seq = seq, Style = p.Style, Title = p.Title };

    /// <summary>The buyer's standing a bid document holds: the buyer, the vehicle, and the bid state.</summary>
    public static StoredBid ToStoredBid(this BidDocument d) =>
        new(d.UserId, d.Id, new BidState(d.Amount, d.BidCount, d.WonBuyNow, d.AtMs));
    // #endregion mapping
}
