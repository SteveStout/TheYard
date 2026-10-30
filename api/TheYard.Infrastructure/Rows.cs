namespace TheYard.Infrastructure;

/// <summary>
/// A vehicle as the database holds it. Deliberately not <c>TheYard.Data.Vehicle</c>:
/// the domain record is sealed, has init-only members and exposes its lists as
/// <see cref="IReadOnlyList{T}"/>, all of which are right for a value passed
/// around the application and wrong for something a mapper has to build a row
/// at a time. Keeping them separate is what lets the domain record stay the
/// shape the domain wants (ADR: The relational store).
///
/// <para>Most of what follows is the dataset's own field under the dataset's own
/// name, kept because the wire carries it to the browser under that name and
/// nothing on the server derives it. Where a column is kept for some other
/// reason, its summary says which.</para>
/// </summary>
public sealed class VehicleRow
{
    /// <summary>Where this vehicle sat in the seed file. See YardDbContext for why it is stored.</summary>
    public required int Seq { get; set; }

    /// <summary>The vehicle's id, a GUID, and this table's key: a vehicle already has an id the rest of the system names it by, so there is no second identity to keep in step.</summary>
    public required string Id { get; set; }

    /// <summary>The vehicle identification number. The one string here whose length and alphabet a standard fixes, which is why the schema gives it varchar rather than nvarchar.</summary>
    public required string Vin { get; set; }

    /// <summary>The model year. Kept as a number because the listing sorts and filters on a range of them.</summary>
    public required int Year { get; set; }

    /// <summary>The manufacturer, such as Mazda. Kept because the listing filters on it and the catalogue's search words are built from it.</summary>
    public required string Make { get; set; }

    /// <summary>The model, such as CX-5. Kept for the same two reasons as the make beside it: a filter, and a search word.</summary>
    public required string Model { get; set; }

    /// <summary>The trim level. Kept because it is part of how a buyer tells two otherwise identical listings apart.</summary>
    public required string Trim { get; set; }

    /// <summary>The body style, such as SUV. Kept because it filters the listing and chooses which pool of stock photos the gallery is drawn from.</summary>
    public required string BodyStyle { get; set; }

    /// <summary>The exterior colour. Kept because the listing shows it and nothing derives it.</summary>
    public required string ExteriorColor { get; set; }

    /// <summary>The interior colour. Kept for the same reason as the exterior colour beside it.</summary>
    public required string InteriorColor { get; set; }

    /// <summary>The engine, such as 2.5L I4. Kept as the dataset's own text: the site shows it and never parses it.</summary>
    public required string Engine { get; set; }

    /// <summary>The transmission, such as automatic. Kept as the dataset's own text, shown and not parsed.</summary>
    public required string Transmission { get; set; }

    /// <summary>The drivetrain, such as FWD. Kept as the dataset's own text, shown and not parsed.</summary>
    public required string Drivetrain { get; set; }

    /// <summary>The odometer reading, in kilometres. Kept as a number because the listing sorts and filters on a range of them.</summary>
    public required int OdometerKm { get; set; }

    /// <summary>The fuel type, such as gasoline. Kept because the listing filters on it.</summary>
    public required string FuelType { get; set; }

    /// <summary>The condition grade the dataset gives it, where higher is better. Kept as the number rather than the band, because the page bands it for display and the listing sorts on the number.</summary>
    public required double ConditionGrade { get; set; }

    /// <summary>The inspector's written condition report. Kept because it is what a buyer reads before bidding, and no shorter field stands in for it.</summary>
    public required string ConditionReport { get; set; }

    /// <summary>A primitive collection: EF stores it as a JSON column on SQLite.</summary>
    public required List<string> DamageNotes { get; set; }

    /// <summary>The title status, such as clean. Kept because it is the one legal fact about the vehicle the listing has to show.</summary>
    public required string TitleStatus { get; set; }

    /// <summary>The province the vehicle is in. Kept because the listing filters on it.</summary>
    public required string Province { get; set; }

    /// <summary>The city the vehicle is in. Kept because the listing shows where the vehicle is, and the province alone is too coarse to be of use.</summary>
    public required string City { get; set; }

    /// <summary>The dataset's own auction date, as the text the dataset holds. Kept as text on purpose: the auction window comes from the id's hash and nothing is derived from this string, so parsing it would invent a second schedule.</summary>
    public required string AuctionStart { get; set; }

    /// <summary>The opening bid, in whole dollars. Kept because it is the floor the bid rules work from until the first bid arrives.</summary>
    public required int StartingBid { get; set; }

    /// <summary>The seller's reserve, in whole dollars, or null when there is none. Kept on the server and never sent: the wire carries whether the reserve is met and the amount stays here.</summary>
    public required int? ReservePrice { get; set; }

    /// <summary>The buy-now price, in whole dollars, or null when there is no buy-now option. Kept because a bid at or above it wins at this price, which is the check the rules make before every other one.</summary>
    public required int? BuyNowPrice { get; set; }

    /// <summary>The gallery's photo paths. A primitive collection like the damage notes: read and written whole, never queried into, which is the case a JSON column is for.</summary>
    public required List<string> Images { get; set; }

    /// <summary>The dealership selling the vehicle. Kept because the listing shows who is selling, which is part of what a bid is placed on trust of.</summary>
    public required string SellingDealership { get; set; }

    /// <summary>The lot number, such as A-0001. Kept because it is how a vehicle is called out on the floor, which no derived id stands in for.</summary>
    public required string Lot { get; set; }

    /// <summary>The price the seed came with, or null when the seed had no bid on it. The seed's figure, kept because the catalogue is filled from it; once the site is running, <see cref="StandingRow"/> is the authority on what a vehicle stands at.</summary>
    public required int? CurrentBid { get; set; }

    /// <summary>How many bids the seed came with. The seed's figure, kept for the same reason as the price beside it, and superseded by the standing in the same way.</summary>
    public required int BidCount { get; set; }
}

/// <summary>One row of the vendored photo manifest.</summary>
public sealed class PhotoRow
{
    /// <summary>Where this photo sat in the manifest. Kept for the same reason as a vehicle's: a table has no order of its own, and the gallery a vehicle gets must be the same one every time.</summary>
    public required int Seq { get; set; }

    /// <summary>The photo's file name, and this table's key: a photo already has a unique name, so a surrogate key would be a second identity to keep in step with the first.</summary>
    public required string File { get; set; }

    /// <summary>The body style pool the photo belongs to. Kept because it is how a vehicle's gallery is drawn from photos of vehicles that look like it.</summary>
    public required string Style { get; set; }

    /// <summary>The source title of the photo, which names the make pictured. Kept because it is the one record of what the photo actually shows, which the style alone does not say.</summary>
    public required string Title { get; set; }
}

/// <summary>
/// One buyer's standing on one vehicle, and the only thing in this database
/// that changes after startup. One row per buyer per vehicle: a later bid from
/// the same person replaces their earlier one rather than appending, because
/// what the application asks is "where do I stand", never "what did I bid an
/// hour ago" (ADR: Accounts and per-user bids).
/// </summary>
public sealed class BidRow
{
    /// <summary>The account that placed it. Half of this row's key.</summary>
    public required string UserId { get; set; }

    /// <summary>The vehicle it was placed on, and the other half of the key: one person has one standing bid per vehicle, so the pair is the identity and no surrogate is needed.</summary>
    public required string VehicleId { get; set; }

    /// <summary>What this buyer stands at, in whole dollars. Kept because it is the answer to the only question asked of this table: where does this person stand on this vehicle.</summary>
    public required int Amount { get; set; }

    /// <summary>How many bids this buyer has placed on this vehicle. Kept because the row replaces rather than appends, so without a count there would be no record that a buyer bid more than once.</summary>
    public required int BidCount { get; set; }

    /// <summary>Whether this buyer is the one who bought the vehicle outright. Kept because a buy-now wins at the buy-now price rather than at the amount bid, so the amount alone does not say what happened.</summary>
    public required bool WonBuyNow { get; set; }

    /// <summary>When the bid was placed, in milliseconds since the epoch, UTC. The unit every recorded instant here uses, which is the one the browser's own clock reads.</summary>
    public required long AtMs { get; set; }

    /// <summary>
    /// The optimistic concurrency token (ADR: The SQL Server backend). On SQL
    /// Server this is a <c>rowversion</c> the database maintains; on SQLite the
    /// store assigns a new value on every save, because SQLite has no such type.
    /// It guards this one buyer's row, so the race it catches is the same
    /// account writing from two sites at once. Two different buyers are two
    /// rows and never meet here; what they race for is the vehicle's standing,
    /// which <see cref="StandingRow"/> guards with a version of its own.
    /// Nullable because a row that has not been read yet does not have one.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// One vehicle's standing across everybody: the price, the count, the high
/// bidder and whether it has sold. The store owns it so two sites over one
/// database agree on it; a write names the version it read and is refused if
/// another writer moved it first (ADR: Three readers with no memory of the
/// project, item 5).
/// </summary>
public sealed class StandingRow
{
    /// <summary>The vehicle, and the key: one standing per vehicle.</summary>
    public required string VehicleId { get; set; }

    /// <summary>What the vehicle stands at across everybody, in whole dollars. Kept by the store rather than worked out from the bids, because two sites over one database have to agree on it without reading every bid to find out.</summary>
    public required int Amount { get; set; }

    /// <summary>How many bids the vehicle has had across everybody. Kept beside the price for the same reason: the listing shows both, and counting the bid rows to get it would be a second read of a table this one exists to avoid reading.</summary>
    public required int BidCount { get; set; }

    /// <summary>The account holding the highest bid.</summary>
    public required string HighBidderId { get; set; }

    /// <summary>Whether anybody has bought the vehicle outright. Kept here, where every site reads it, because a sold vehicle is sold to everybody and that check runs before all the other bid rules.</summary>
    public required bool SoldBuyNow { get; set; }

    /// <summary>When the standing last moved, in milliseconds since the epoch, UTC. Kept so a reader can say how old the figure beside it is without going to the bids to find out.</summary>
    public required long AtMs { get; set; }

    /// <summary>
    /// Moves by one on every write. A plain number rather than rowversion, so
    /// the compare-and-set reads the same on SQL Server and SQLite: an update
    /// that names the version it read and changes no row lost the race.
    /// </summary>
    public required long Version { get; set; }
}
