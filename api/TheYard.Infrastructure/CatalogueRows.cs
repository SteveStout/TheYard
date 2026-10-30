// The Catalogue table's row, its part of the model, and the copy between the row and the domain
// record. One file because the three change together: a field added to the vehicle is a column,
// a line of model and a line of mapping, and a reader checking that nothing is dropped finds all
// three here (ADR: The store does the work).
using TheYard.Application.Shared.Auction;
using TheYard.Data;
using TheYard.Domain.Shared.Auction;

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

    /// <summary>The vehicle's id, a GUID. Kept because the slot its hash gives it, and every other table that names a vehicle, both start from this.</summary>
    public required string Id { get; set; }

    /// <summary>The vehicle identification number. Kept because it is one of the columns a typed word is searched in, and the one identifier a buyer can check against the vehicle itself.</summary>
    public required string Vin { get; set; }

    /// <summary>The model year. Kept as a number on the row because the listing filters a range of them off an index.</summary>
    public required int Year { get; set; }

    /// <summary>The manufacturer, such as Mazda. Kept because the listing filters on it and it is part of the searchable text.</summary>
    public required string Make { get; set; }

    /// <summary>The model, such as CX-5. Kept for the same two reasons as the make beside it: a filter, and a searched word.</summary>
    public required string Model { get; set; }

    /// <summary>The trim level. Kept because it is searched and shown, and it is how two otherwise identical listings are told apart.</summary>
    public required string Trim { get; set; }

    /// <summary>The body style, such as SUV. Kept because the listing filters on it and it chooses which pool of stock photos the gallery is drawn from.</summary>
    public required string BodyStyle { get; set; }

    /// <summary>The exterior colour. Kept because a page of the listing is answered from this row alone, and the card shows it.</summary>
    public required string ExteriorColor { get; set; }

    /// <summary>The interior colour. Kept for the same reason as the exterior colour beside it: the detail page is served from this row.</summary>
    public required string InteriorColor { get; set; }

    /// <summary>The engine, such as 2.5L I4. Kept as the dataset's own text, shown and searched and never parsed.</summary>
    public required string Engine { get; set; }

    /// <summary>The transmission, such as automatic. Kept as the dataset's own text, shown and never parsed.</summary>
    public required string Transmission { get; set; }

    /// <summary>The drivetrain, such as FWD. Kept as the dataset's own text, shown and never parsed.</summary>
    public required string Drivetrain { get; set; }

    /// <summary>The odometer reading, in kilometres. Kept as a number because the listing sorts and filters a range of them off an index.</summary>
    public required int OdometerKm { get; set; }

    /// <summary>The fuel type, such as gasoline. Kept because the listing filters on it.</summary>
    public required string FuelType { get; set; }

    /// <summary>The condition grade, where higher is better. Kept as the number rather than the band, because the listing sorts on the number and the page bands it for display.</summary>
    public required double ConditionGrade { get; set; }

    /// <summary>The inspector's written condition report. Kept because it is what a buyer reads before bidding, and the detail page is answered from this row.</summary>
    public required string ConditionReport { get; set; }

    /// <summary>A primitive collection: EF stores it as a JSON column.</summary>
    public required List<string> DamageNotes { get; set; }

    /// <summary>The title status, such as clean. Kept because it is the one legal fact about the vehicle every listing has to show.</summary>
    public required string TitleStatus { get; set; }

    /// <summary>The province the vehicle is in. Kept because the listing filters on it.</summary>
    public required string Province { get; set; }

    /// <summary>The city the vehicle is in. Kept because the card says where the vehicle is, and the province alone is too coarse to be of use.</summary>
    public required string City { get; set; }

    /// <summary>The dataset's own auction date, as the text the dataset holds. Kept as text on purpose: the window is the slot beside it, derived from the id's hash, and parsing this would invent a second schedule.</summary>
    public required string AuctionStart { get; set; }

    /// <summary>The opening bid, in whole dollars. Kept because it is the floor the rules work from until the first bid, and half of what the price column is derived from.</summary>
    public required int StartingBid { get; set; }

    /// <summary>The seller's reserve, in whole dollars, or null when there is none. Kept on the server and never sent: the wire carries whether the reserve is met and the amount stays here.</summary>
    public required int? ReservePrice { get; set; }

    /// <summary>The buy-now price, in whole dollars, or null when there is no buy-now option. Kept because a bid at or above it wins at this price, which the rules check before everything else.</summary>
    public required int? BuyNowPrice { get; set; }

    /// <summary>The dataset's images, a JSON column, kept for a body style the gallery has no pool for.</summary>
    public required List<string> Images { get; set; }

    /// <summary>The dealership selling the vehicle. Kept because the listing shows who is selling, which is part of what a bid is placed on trust of.</summary>
    public required string SellingDealership { get; set; }

    /// <summary>The lot number, such as A-0001. Kept because it is how a vehicle is called out on the floor, and no derived id stands in for it.</summary>
    public required string Lot { get; set; }

    /// <summary>The price the dataset came with, or null when it had no bid. Kept because the price column is derived from it at the fill; once the site is running, the standing the store owns is the authority on what a vehicle stands at.</summary>
    public required int? CurrentBid { get; set; }

    /// <summary>How many bids the dataset came with. Kept beside the price it came with, and superseded by the standing in the same way.</summary>
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
