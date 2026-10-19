// The words a store-side catalogue query is written in: a filter with every value already
// resolved to the store's own spelling, the order a page is read in, the three schedule segments,
// and the slot each vehicle's id gives it. The adapters translate these into EF Core and Cosmos DB
// SQL; the in-memory catalogue answers them over a list. Application plans every listing out of
// them, so the planning has one copy (ADR: The store does the work).
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>One vehicle and its place in the expansion, which breaks every tie in every order.</summary>
/// <param name="Seq">Where the vehicle sits in the deterministic expansion, from 0.</param>
/// <param name="Vehicle">The vehicle as the dataset has it, before anybody's bid.</param>
public sealed record CatalogueEntry(int Seq, Vehicle Vehicle);

/// <summary>How a store is filled: a batch at a time, a pause between batches, and the most it fills while warming.</summary>
/// <param name="Batch">Vehicles written in one call.</param>
/// <param name="Pause">The wait between batches, so a fill on a live account leaves throughput for visitors.</param>
/// <param name="FillAtWarmUpTo">The most missing vehicles the warm-up writes before answering; past it, the fill runs beside the site.</param>
public sealed record CatalogueFillPace(int Batch, TimeSpan Pause, int FillAtWarmUpTo);

/// <summary>Matches, split by where each auction stands on the clock.</summary>
/// <param name="Live">Started and not yet ended.</param>
/// <param name="Upcoming">Not yet started.</param>
/// <param name="Ended">Ended.</param>
public sealed record SegmentCounts(int Live, int Upcoming, int Ended)
{
    /// <summary>No matches at all.</summary>
    public static readonly SegmentCounts None = new(0, 0, 0);

    /// <summary>Every match, whatever its segment.</summary>
    public int Total => Live + Upcoming + Ended;
}

/// <summary>Which of the three schedule segments a filter keeps.</summary>
public enum CatalogueSegment
{
    /// <summary>Every segment.</summary>
    Any,

    /// <summary>Started and not yet ended: start offset at or before now, end offset after it.</summary>
    Live,

    /// <summary>Start offset after now.</summary>
    Upcoming,

    /// <summary>End offset at or before now.</summary>
    Ended,
}

/// <summary>
/// The orders a page is read in, each one the store can serve off an index, and each one
/// broken by the expansion's place so a tie reads the same on every store.
/// </summary>
public enum CatalogueOrder
{
    /// <summary>The expansion's own order.</summary>
    Seq,

    /// <summary>The live segment, closest end first.</summary>
    EndAscending,

    /// <summary>The upcoming segment, soonest start first.</summary>
    StartAscending,

    /// <summary>The ended segment, most recent end first.</summary>
    EndDescending,

    /// <summary>The price a buyer competes against, lowest first.</summary>
    PriceAscending,

    /// <summary>The price a buyer competes against, highest first.</summary>
    PriceDescending,

    /// <summary>The inspection grade, best first.</summary>
    ConditionDescending,

    /// <summary>The dataset's bid count, most first.</summary>
    BidsDescending,
}

/// <summary>One word the visitor typed, and the segments whose status word holds it, so "live" finds the live auctions as it always did.</summary>
/// <param name="Text">The word, lowercased.</param>
/// <param name="AlsoMatches">The segments whose status word ("live", "upcoming", "ended") contains it.</param>
public sealed record CatalogueToken(string Text, IReadOnlyList<CatalogueSegment> AlsoMatches);

// #region catalogue-filter
/// <summary>
/// A visitor's filter in the store's terms. Every value is the store's own spelling, resolved
/// against the facets, so the adapters compare with plain equality; the clock is the one number
/// the slots are compared against, milliseconds since the day's anchor.
/// </summary>
public sealed record CatalogueFilter
{
    /// <summary>Now, as milliseconds since the UTC midnight that began the day, the one number a slot is compared with.</summary>
    public long SinceAnchorMs { get; init; }

    /// <summary>The make, exactly as the store holds it, or null for any.</summary>
    public string? Make { get; init; }

    /// <summary>The body style, exactly as the store holds it, or null for any.</summary>
    public string? BodyStyle { get; init; }

    /// <summary>The title status, exactly as the store holds it, or null for any.</summary>
    public string? TitleStatus { get; init; }

    /// <summary>The province, exactly as the store holds it, or null for any.</summary>
    public string? Province { get; init; }

    /// <summary>The segment kept.</summary>
    public CatalogueSegment Segment { get; init; }

    /// <summary>The lowest inspection grade kept, or null.</summary>
    public double? MinCondition { get; init; }

    /// <summary>The lowest price kept, on the dataset's figure, or null.</summary>
    public int? PriceMin { get; init; }

    /// <summary>The highest price kept, on the dataset's figure, or null.</summary>
    public int? PriceMax { get; init; }

    /// <summary>Every word the visitor typed; each must appear in the searchable text or name the vehicle's segment.</summary>
    public IReadOnlyList<CatalogueToken> Tokens { get; init; } = [];

    /// <summary>Ids the store leaves out, because somebody's bid moved their price and the page works them out in memory.</summary>
    public IReadOnlyCollection<string> Exclude { get; init; } = [];

    /// <summary>The same filter kept to one segment.</summary>
    public CatalogueFilter In(CatalogueSegment segment) => this with { Segment = segment };

    /// <summary>A key that names this filter for the listing cache, without the clock, which the cache buckets separately.</summary>
    public string Key() => string.Join('\u001f',
        Make, BodyStyle, TitleStatus, Province, Segment, MinCondition, PriceMin, PriceMax,
        string.Join(' ', Tokens.Select(token => token.Text)),
        Exclude.Count == 0 ? "" : ExcludeHash());

    private string ExcludeHash()
    {
        uint hash = 2_166_136_261;
        foreach (string id in Exclude.Order(StringComparer.Ordinal))
        {
            hash = (hash ^ Fnv1a.Hash(id)) * 16_777_619;
        }
        return $"{Exclude.Count}:{hash:x8}";
    }

    // #region in-memory
    /// <summary>
    /// The same filter over one vehicle in memory: what the in-memory catalogue runs down its
    /// list, and the answer every adapter's query is held to by the tests.
    /// </summary>
    public bool Matches(CatalogueEntry entry, CatalogueSlot slot, string searchText)
    {
        var vehicle = entry.Vehicle;
        if (Exclude.Count > 0 && Exclude.Contains(vehicle.Id))
        {
            return false;
        }
        if ((Make is not null && !string.Equals(vehicle.Make, Make, StringComparison.Ordinal)) ||
            (BodyStyle is not null && !string.Equals(vehicle.BodyStyle, BodyStyle, StringComparison.Ordinal)) ||
            (TitleStatus is not null && !string.Equals(vehicle.TitleStatus, TitleStatus, StringComparison.Ordinal)) ||
            (Province is not null && !string.Equals(vehicle.Province, Province, StringComparison.Ordinal)))
        {
            return false;
        }
        var segment = slot.SegmentAt(SinceAnchorMs);
        if (Segment != CatalogueSegment.Any && segment != Segment)
        {
            return false;
        }
        if (MinCondition is { } grade && vehicle.ConditionGrade < grade)
        {
            return false;
        }
        int price = CatalogueSlot.PriceOf(vehicle);
        if ((PriceMin is { } low && price < low) || (PriceMax is { } high && price > high))
        {
            return false;
        }
        foreach (var token in Tokens)
        {
            if (!searchText.Contains(token.Text, StringComparison.Ordinal) && !token.AlsoMatches.Contains(segment))
            {
                return false;
            }
        }
        return true;
    }
    // #endregion in-memory
}
// #endregion catalogue-filter

// #region slot
/// <summary>
/// The slot a vehicle's id gives it: where its window starts and ends, as milliseconds from
/// the day's anchor. <see cref="AuctionSchedule.Window"/> places every window at the anchor plus
/// an offset that depends on the id alone, so the offsets are the same every day and can be
/// written down beside the vehicle, which is what lets the store order a page by auction time.
/// The hash stays the authority: a test reads the slot back for every vehicle and holds it to the
/// window the hash gives on several anchors (ADR: The store does the work).
/// </summary>
/// <param name="StartOffsetMs">The window's start, milliseconds from the anchor; negative before it.</param>
/// <param name="EndOffsetMs">The window's end, milliseconds from the anchor.</param>
public readonly record struct CatalogueSlot(long StartOffsetMs, long EndOffsetMs)
{
    /// <summary>The slot for an id: the window the schedule gives it on an anchor of zero.</summary>
    public static CatalogueSlot For(string vehicleId)
    {
        var window = AuctionSchedule.Window(vehicleId, 0);
        return new CatalogueSlot(window.StartsAtMs, window.EndsAtMs);
    }

    /// <summary>The price a buyer competes against on the dataset's own figures: the current bid, or the opening ask.</summary>
    public static int PriceOf(Vehicle vehicle) => vehicle.CurrentBid ?? vehicle.StartingBid;

    /// <summary>Where this auction stands at a moment given as milliseconds since the anchor, by the schedule's own rule.</summary>
    public CatalogueSegment SegmentAt(long sinceAnchorMs) =>
        sinceAnchorMs < StartOffsetMs ? CatalogueSegment.Upcoming
        : sinceAnchorMs < EndOffsetMs ? CatalogueSegment.Live
        : CatalogueSegment.Ended;
}
// #endregion slot
