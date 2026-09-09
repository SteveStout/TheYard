using System.Text.Json;
using System.Text.Json.Nodes;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// The wire shape of a vehicle: the dataset fields plus the server-derived
/// auction facts (window, status, minimum next bid, and whether it is sold).
/// Deriving these once, server-side, keeps the client from re-implementing
/// schedule math. The browser only formats and counts down.
/// </summary>
public static class VehicleWire
{
    // #region sold
    /// <summary>
    /// <paramref name="sold"/> is not a default parameter on purpose: every
    /// caller has the bid service in hand and has to say, because a listing
    /// that forgot would show a bought vehicle as open to everybody but its
    /// buyer, which is what every listing did until 1.0.0.110 (ADR: Accounts
    /// and per-user bids, the addendum on the second buyer).
    /// </summary>
    public static JsonObject ToWire(Vehicle vehicle, AuctionClock clock, JsonSerializerOptions options, bool sold)
    {
        var node = JsonSerializer.SerializeToNode(vehicle, options)!.AsObject();
        var window = AuctionSchedule.Window(vehicle.Id, clock.AnchorMs);
        node["auction_starts_at"] = window.StartsAtMs;
        node["auction_ends_at"] = window.EndsAtMs;
        node["auction_status"] = AuctionSchedule.Status(window, clock.NowMs).ToString().ToLowerInvariant();
        node["min_next_bid"] = BidRules.MinNextBid(vehicle);
        // The status stays the clock's, because the browser recomputes it from
        // the window as time passes and would overwrite a status that said
        // otherwise. Sold rides beside it as its own fact.
        node["sold"] = sold;
        return node;
    }
    // #endregion sold
}
