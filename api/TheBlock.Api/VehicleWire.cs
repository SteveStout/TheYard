using System.Text.Json;
using System.Text.Json.Nodes;
using TheBlock.Domain;
using TheBlock.Data;

namespace TheBlock.Api;

/// <summary>
/// The wire shape of a vehicle: the dataset fields plus the server-derived
/// auction facts (window, status, minimum next bid). Deriving these once,
/// server-side, keeps the client from re-implementing schedule math — the
/// browser only formats and counts down.
/// </summary>
public static class VehicleWire
{
    public static JsonObject ToWire(Vehicle vehicle, AuctionClock clock, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(vehicle, options)!.AsObject();
        var window = AuctionSchedule.Window(vehicle.Id, clock.AnchorMs);
        node["auction_starts_at"] = window.StartsAtMs;
        node["auction_ends_at"] = window.EndsAtMs;
        node["auction_status"] = AuctionSchedule.Status(window, clock.NowMs).ToString().ToLowerInvariant();
        node["min_next_bid"] = BidRules.MinNextBid(vehicle);
        return node;
    }
}
