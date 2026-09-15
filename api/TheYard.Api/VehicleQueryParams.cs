using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// GET-parameter binding for /api/vehicles. The wire names mirror the
/// payload's snake_case fields. Translates itself into the domain's
/// VehicleFilter plus the AuctionClock statuses are evaluated against,
/// rejecting unknown status and sort values. The clock is the server's; an
/// anchor a request still sends is not read.
///
/// <para>The attributes target the properties, not the constructor
/// parameters, on purpose. An [AsParameters] record is read through a
/// parameter wrapper that merges the constructor's attributes with the
/// property's into a plain Attribute array whenever the constructor has any,
/// and the document generator then casts that array to a typed one and
/// throws. With the attributes on the properties the wrapper hands back the
/// property's own typed array and the document builds (ADR: The API
/// describes itself).</para>
/// </summary>
public sealed record VehicleQueryParams(
    [property: FromQuery(Name = "q"), Description("Free text, matched against every filterable field and the derived auction status.")] string? Q,
    [property: FromQuery(Name = "make"), Description("One make, exactly as the facets list it.")] string? Make,
    [property: FromQuery(Name = "body_style"), Description("One body style, exactly as the facets list it.")] string? BodyStyle,
    [property: FromQuery(Name = "title_status"), Description("One title status, exactly as the facets list it.")] string? TitleStatus,
    [property: FromQuery(Name = "province"), Description("One province, exactly as the facets list it.")] string? Province,
    [property: FromQuery(Name = "status"), Description("live, upcoming or ended, on the server's clock. Anything else is a 400.")] string? Status,
    [property: FromQuery(Name = "sort"), Description("ending-soonest (the default), price-asc, price-desc, condition or most-bids. Anything else is a 400.")] string? Sort,
    [property: FromQuery(Name = "limit"), Description("Page size, 1 to 500; 100 when absent.")] int? Limit,
    [property: FromQuery(Name = "offset"), Description("How many matches to skip; 0 when absent.")] int? Offset,
    [property: FromQuery(Name = "min_condition"), Description("The lowest condition grade to include.")] double? MinCondition,
    [property: FromQuery(Name = "price_min"), Description("The lowest standing price to include, in whole dollars.")] double? PriceMin,
    [property: FromQuery(Name = "price_max"), Description("The highest standing price to include, in whole dollars.")] double? PriceMax)
{
    /// <summary>The landing page shows the top 100; clients may ask for up to 500.</summary>
    public const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public int EffectiveLimit => Math.Clamp(Limit ?? DefaultLimit, 1, MaxLimit);

    public int EffectiveOffset => Math.Max(Offset ?? 0, 0);

    public bool TryBuildFilter(
        out VehicleFilter filter,
        out AuctionClock clock,
        out VehicleSort sort,
        out string? error)
    {
        filter = new VehicleFilter();
        sort = VehicleSort.EndingSoonest;
        clock = Clocks.Now();

        // Explicit name matching, because Enum.TryParse would also accept numeric
        // strings ("9") and comma lists ("live,ended"), which should be 400s.
        AuctionStatus? status = Status?.ToLowerInvariant() switch
        {
            "live" => AuctionStatus.Live,
            "upcoming" => AuctionStatus.Upcoming,
            "ended" => AuctionStatus.Ended,
            _ => null,
        };
        if (!string.IsNullOrEmpty(Status) && status is null)
        {
            error = $"Unknown status '{Status}'. Use live, upcoming, or ended.";
            return false;
        }

        VehicleSort? parsedSort = Sort?.ToLowerInvariant() switch
        {
            null or "" or "ending-soonest" => VehicleSort.EndingSoonest,
            "price-asc" => VehicleSort.PriceAsc,
            "price-desc" => VehicleSort.PriceDesc,
            "condition" => VehicleSort.Condition,
            "most-bids" => VehicleSort.MostBids,
            _ => null,
        };
        if (parsedSort is null)
        {
            error = $"Unknown sort '{Sort}'. Use ending-soonest, price-asc, price-desc, condition, or most-bids.";
            return false;
        }
        sort = parsedSort.Value;

        filter = new VehicleFilter
        {
            Query = Q,
            Make = Make,
            BodyStyle = BodyStyle,
            TitleStatus = TitleStatus,
            Province = Province,
            Status = status,
            MinCondition = MinCondition,
            // Prices are whole dollars; accept decimal input but keep integer
            // semantics (min rounds up, max rounds down) and clamp to int range.
            PriceMin = ToIntBound(PriceMin, roundUp: true),
            PriceMax = ToIntBound(PriceMax, roundUp: false),
        };
        error = null;
        return true;
    }

    private static int? ToIntBound(double? value, bool roundUp)
    {
        if (value is not { } bound || !double.IsFinite(bound))
        {
            return null;
        }
        double rounded = roundUp ? Math.Ceiling(bound) : Math.Floor(bound);
        return (int)Math.Clamp(rounded, 0, int.MaxValue);
    }
}
