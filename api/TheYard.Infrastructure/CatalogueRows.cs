// The Catalogue table's row, its part of the model, and the copy between the row and the domain
// record. One file because the three change together: a field added to the vehicle is a column,
// a line of model and a line of mapping, and a reader checking that nothing is dropped finds all
// three here (ADR: The store does the work).
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Infrastructure;

/// <summary>
/// One vehicle of the hundred thousand, as the Catalogue table holds it: the dataset's fields,
/// its place in the expansion, the slot its id's hash gives it, the price a buyer competes
/// against on the dataset's figures, and its searchable text, the last four written once at the
/// fill so the store can filter and order on them off an index.
/// </summary>
public sealed class CatalogueRow
{
    /// <summary>Where this vehicle sits in the deterministic expansion; the clustered key and every order's tie-break.</summary>
    public required int Seq { get; set; }

    public required string Id { get; set; }

    public required string Vin { get; set; }

    public required int Year { get; set; }

    public required string Make { get; set; }

    public required string Model { get; set; }

    public required string Trim { get; set; }

    public required string BodyStyle { get; set; }

    public required string ExteriorColor { get; set; }

    public required string InteriorColor { get; set; }

    public required string Engine { get; set; }

    public required string Transmission { get; set; }

    public required string Drivetrain { get; set; }

    public required int OdometerKm { get; set; }

    public required string FuelType { get; set; }

    public required double ConditionGrade { get; set; }

    public required string ConditionReport { get; set; }

    /// <summary>A primitive collection: EF stores it as a JSON column.</summary>
    public required List<string> DamageNotes { get; set; }

    public required string TitleStatus { get; set; }

    public required string Province { get; set; }

    public required string City { get; set; }

    public required string AuctionStart { get; set; }

    public required int StartingBid { get; set; }

    public required int? ReservePrice { get; set; }

    public required int? BuyNowPrice { get; set; }

    /// <summary>The dataset's images, a JSON column, kept for a body style the gallery has no pool for.</summary>
    public required List<string> Images { get; set; }

    public required string SellingDealership { get; set; }

    public required string Lot { get; set; }

    public required int? CurrentBid { get; set; }

    public required int BidCount { get; set; }

    /// <summary>The window's start, milliseconds from the day's anchor (CatalogueSlot).</summary>
    public required long StartOffsetMs { get; set; }

    /// <summary>The window's end, milliseconds from the day's anchor (CatalogueSlot).</summary>
    public required long EndOffsetMs { get; set; }

    /// <summary>The current bid, or the opening ask: what the price filters and sorts read.</summary>
    public required int Price { get; set; }

    /// <summary>The lowercased searchable text (VehicleSearchIndex.TextFor), what a typed word is looked for in.</summary>
    public required string SearchText { get; set; }
}

/// <summary>The Catalogue table's mapping and its part of the model.</summary>
public static class CatalogueRows
{
    /// <summary>The longest searchable text: every searched column at its width, and the spaces between.</summary>
    public const int SearchTextLength = 400;

    // #region catalogue-mapping
    /// <summary>The row for one vehicle at its place, with the slot, the price and the text derived here and nowhere else.</summary>
    public static CatalogueRow ToCatalogueRow(this CatalogueEntry entry)
    {
        var v = entry.Vehicle;
        var slot = CatalogueSlot.For(v.Id);
        return new CatalogueRow
        {
            Seq = entry.Seq,
            Id = v.Id,
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
            Images = [.. v.Images],
            SellingDealership = v.SellingDealership,
            Lot = v.Lot,
            CurrentBid = v.CurrentBid,
            BidCount = v.BidCount,
            StartOffsetMs = slot.StartOffsetMs,
            EndOffsetMs = slot.EndOffsetMs,
            Price = CatalogueSlot.PriceOf(v),
            SearchText = VehicleSearchIndex.TextFor(v),
        };
    }

    /// <summary>The vehicle and its place a stored row holds, every dataset field copied across.</summary>
    public static CatalogueEntry ToEntry(this CatalogueRow row) => new(row.Seq, new Vehicle
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
    });
    // #endregion catalogue-mapping
}
