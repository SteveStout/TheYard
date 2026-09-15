using System.ComponentModel;

namespace TheYard.Api;

// What the endpoints answer with, as named shapes (ADR: The API describes
// itself). Until 1.0.0.137 each of these was an anonymous object inside its
// handler, which the wire did not mind and the document could only describe
// as "an object". The names are the wire's names, snake_cased by the
// serializer like everything else here; the field order is the order the
// anonymous objects had, so nothing on the wire moved.

/// <summary>One page of the catalogue: how many vehicles matched, and the page of them asked for.</summary>
public sealed record VehiclePage(
    [property: Description("How many vehicles match the whole query, not how many are on this page.")] int Total,
    IReadOnlyList<VehicleView> Vehicles);

/// <summary>
/// What a bid or a purchase answers: what happened, the amount it stands at,
/// the caller's badge for this vehicle, and the vehicle as the page should now
/// show it.
/// </summary>
public sealed record BidResult(
    [property: Description("accepted, or won when the vehicle was bought outright, at the buy-now price or by a bid that reached it.")] string Kind,
    [property: Description("The amount the bid stands at, in whole dollars.")] int Amount,
    [property: Description("The caller's standing on this vehicle after the bid, or null if a reset landed between the bid and this read.")] BidView? Bid,
    VehicleView Vehicle);

/// <summary>One line of the account page's history: the vehicle, its title, and the caller's bid on it.</summary>
public sealed record BidHistoryEntry(
    string VehicleId,
    [property: Description("Year, make and model, or (withdrawn) if the vehicle has left the catalogue.")] string Title,
    BidView Bid);

/// <summary>The account page's history, newest first.</summary>
public sealed record BidHistory(int Count, IReadOnlyList<BidHistoryEntry> Bids);

/// <summary>One round of the room's bidding: how many auctions it raised, and the caller's badges afterwards.</summary>
public sealed record TickResult(
    [property: Description("How many auctions the room raised this round.")] int Raised,
    [property: Description("The caller's standing on every vehicle they have bid on, keyed by vehicle id.")] IReadOnlyDictionary<string, BidView> Bids);

/// <summary>The build this container was made from (ADR-005).</summary>
public sealed record BuildInfo(
    [property: Description("The changelog's top line when this build was made, or dev outside a build.")] string Version,
    [property: Description("The short commit, or local outside a build.")] string Commit);

/// <summary>The Admin tab's health card: every check, timed, and the build that answered.</summary>
public sealed record HealthReport(
    [property: Description("healthy when every check passes, degraded otherwise.")] string Status,
    long UptimeSeconds,
    string Version,
    string Commit,
    IReadOnlyList<HealthCheckEntry> Checks);

/// <summary>The one sentence a forgot-password request answers, whether or not the address has an account.</summary>
public sealed record ForgotReply(bool Sent, string Message);
