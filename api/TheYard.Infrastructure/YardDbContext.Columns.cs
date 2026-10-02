// The column widths and the one value converter the relational model uses, named once so the
// model and the tests that hold it to the SQL project read the same numbers. They have a file of their own
// because they are facts about the schema rather than steps in building it, and the model in
// YardDbContext.cs reads more plainly without them in the way.
namespace TheYard.Infrastructure;

/// <summary>The relational store's named column widths and its auction-start converter.</summary>
public sealed partial class YardDbContext
{
    /// <summary>"sql" and "cosmos" are the keys; the width leaves room for a third store and not for a sentence.</summary>
    public const int StoreKeyLength = 16;

    /// <summary>An IPv4 network cut to three octets and an x is at most 12 characters; an IPv6 prefix cut the same way fits in 32.</summary>
    public const int NetworkLength = 40;

    /// <summary>Twenty paths of two hundred characters and their counts, as JSON, fit with room to spare.</summary>
    public const int PathsLength = 4000;

    /// <summary>
    /// Room for a seed vehicle's id (36 characters today) and for the synthetic
    /// ids the scale-up derives from them, which add six more. Bids reference
    /// those, so the two columns are sized together.
    /// </summary>
    public const int IdLength = 64;

    /// <summary>
    /// What this application uses for Identity's key columns, narrowed from
    /// Identity's own default of 450 so that composite keys built from them stay
    /// inside SQL Server's 900-byte clustered index limit.
    /// </summary>
    public const int IdentityKeyLength = 128;

    /// <summary>The dataset's timestamp format: a local wall-clock instant to the second, with no zone.</summary>
    public const string AuctionStartFormat = "yyyy-MM-ddTHH:mm:ss";

    // #region auction-start
    /// <summary>
    /// The catalogue's `auction_start` on the wire is a string and in the
    /// database is a `datetime2(0)`. Round-tripping through this converter is
    /// exact for every row in the dataset, which a test asserts over all two
    /// hundred of them rather than over an example.
    /// </summary>
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string, DateTime>
        AuctionStartToDateTime = new(
            text => DateTime.ParseExact(text, AuctionStartFormat, System.Globalization.CultureInfo.InvariantCulture),
            moment => moment.ToString(AuctionStartFormat, System.Globalization.CultureInfo.InvariantCulture));
    // #endregion auction-start
}
