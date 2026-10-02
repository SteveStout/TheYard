using System.ComponentModel;

namespace TheYard.Api;

// What the endpoints answer with, as named shapes (ADR: The API describes
// itself). An anonymous object inside a handler is a shape the wire does not
// mind and the document can only describe as "an object"; a named one is
// described in full. The names are the wire's names, snake_cased by the
// serializer like everything else here; the field order is the order the
// anonymous objects had, so nothing on the wire moved.

/// <summary>One page of the catalogue: how many vehicles matched, and the page of them asked for.</summary>
/// <param name="Total">How many vehicles match the whole query, not how many are on this page.</param>
/// <param name="Vehicles">The vehicles on this page.</param>
public sealed record VehiclePage(
    [property: Description("How many vehicles match the whole query, not how many are on this page.")] int Total,
    IReadOnlyList<VehicleView> Vehicles);

/// <summary>
/// What a bid or a purchase answers: what happened, the amount it stands at,
/// the caller's badge for this vehicle, and the vehicle as the page should now
/// show it.
/// </summary>
/// <param name="Kind">accepted, or won when the vehicle was bought outright, at the buy-now price or by a bid that reached it.</param>
/// <param name="Amount">The amount the bid stands at, in whole dollars.</param>
/// <param name="Bid">The caller's standing on this vehicle after the bid, or null if a reset landed between the bid and this read.</param>
/// <param name="Vehicle">The vehicle as the page should now show it.</param>
public sealed record BidResult(
    [property: Description("accepted, or won when the vehicle was bought outright, at the buy-now price or by a bid that reached it.")] string Kind,
    [property: Description("The amount the bid stands at, in whole dollars.")] int Amount,
    [property: Description("The caller's standing on this vehicle after the bid, or null if a reset landed between the bid and this read.")] BidView? Bid,
    VehicleView Vehicle);

/// <summary>One line of the account page's history: the vehicle, its title, and the caller's bid on it.</summary>
/// <param name="VehicleId">The id of the vehicle bid on.</param>
/// <param name="Title">Year, make and model, or (withdrawn) if the vehicle has left the catalogue.</param>
/// <param name="Bid">The caller's standing on the vehicle.</param>
public sealed record BidHistoryEntry(
    string VehicleId,
    [property: Description("Year, make and model, or (withdrawn) if the vehicle has left the catalogue.")] string Title,
    BidView Bid);

/// <summary>The account page's history, newest first.</summary>
/// <param name="Count">How many vehicles the caller has bid on.</param>
/// <param name="Bids">One entry per vehicle, newest first.</param>
public sealed record BidHistory(int Count, IReadOnlyList<BidHistoryEntry> Bids);

/// <summary>One round of the room's bidding: how many auctions it raised, and the caller's badges afterwards.</summary>
/// <param name="Raised">How many auctions the room raised this round.</param>
/// <param name="Bids">The caller's standing on every vehicle they have bid on, keyed by vehicle id.</param>
public sealed record TickResult(
    [property: Description("How many auctions the room raised this round.")] int Raised,
    [property: Description("The caller's standing on every vehicle they have bid on, keyed by vehicle id.")] IReadOnlyDictionary<string, BidView> Bids);

/// <summary>The build this container was made from (ADR-005).</summary>
/// <param name="Version">The changelog's top line when this build was made, or dev outside a build.</param>
/// <param name="Commit">The short commit, or local outside a build.</param>
public sealed record BuildInfo(
    [property: Description("The changelog's top line when this build was made, or dev outside a build.")] string Version,
    [property: Description("The short commit, or local outside a build.")] string Commit);

/// <summary>The Admin tab's health card: every check, timed, and the build that answered.</summary>
/// <param name="Status">healthy when every check passes, degraded otherwise.</param>
/// <param name="UptimeSeconds">How long this process has been running, in seconds.</param>
/// <param name="Version">The build version that answered.</param>
/// <param name="Commit">The short commit of the build that answered.</param>
/// <param name="Checks">Every health check, with its result and timing.</param>
/// <param name="KeptWarm">The keep-warm loop's last pass (ADR: Kept awake); null where the loop is off.</param>
public sealed record HealthReport(
    [property: Description("healthy when every check passes, degraded otherwise.")] string Status,
    long UptimeSeconds,
    string Version,
    string Commit,
    IReadOnlyList<HealthCheckEntry> Checks,
    [property: Description("The keep-warm loop's last pass (ADR: Kept awake); null where the loop is off.")] KeepWarmReading? KeptWarm = null);

/// <summary>The keep-warm loop's last pass as the health card shows it: when, how many reads, how many failed, and the slowest.</summary>
/// <param name="LastPass">When the last pass ran, or null before the first one.</param>
/// <param name="Reads">How many reads the pass made.</param>
/// <param name="Failed">How many of those reads failed.</param>
/// <param name="SlowestMs">The slowest read, in milliseconds.</param>
/// <param name="Slowest">The path and store of the slowest read, or null before the first pass.</param>
public sealed record KeepWarmReading(DateTimeOffset? LastPass, int Reads, int Failed, long SlowestMs, string? Slowest)
{
    public static KeepWarmReading Of(KeepWarmPass? pass) =>
        pass is null ? new KeepWarmReading(null, 0, 0, 0, null) : new KeepWarmReading(pass.At, pass.Reads, pass.Failed, pass.SlowestMs, pass.Slowest);
}

/// <summary>The one sentence a forgot-password request answers, whether or not the address has an account.</summary>
/// <param name="Sent">Always true, so the reply does not reveal whether the address has an account.</param>
/// <param name="Message">The sentence the page shows.</param>
public sealed record ForgotReply(bool Sent, string Message);
