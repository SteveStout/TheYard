using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// Bidding and buying now, the caller's standing and history, one round of the
/// simulated room, and one person's start-over. Every write needs a signed-in
/// bidder; the domain's BidRules decide, on the server's clock.
/// </summary>
public static class BidEndpoints
{
    /// <summary>Maps the bidding routes under /api/vehicles/{id}, /api/bids and /api/market.</summary>
    public static IEndpointRouteBuilder MapBidEndpoints(this IEndpointRouteBuilder app)
    {
        #region bid-endpoints
        // Bidding. Every write needs a signed-in bidder, and the domain's BidRules decide on the
        // server's clock. Nothing here is only the caller's to change: a bid moves the price
        // everybody sees, and it is kept in the store.
        app.MapPost("/api/vehicles/{id}/bids", PlaceBid)
            .RequireAuthorization()
            .WithName("PlaceBid")
            .WithTags("Bids")
            .WithSummary("Bid on a vehicle")
            .WithDescription("Whole dollars. The rules run on the server: a sold vehicle takes no bid, an amount at or above "
                + "the buy-now price wins outright at that price, and anything else has to clear the minimum next bid "
                + "measured against the room's standing price. A session bids on the store it was opened on.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // No body: a purchase names the vehicle in its address and nothing else.
        app.MapPost("/api/vehicles/{id}/buy-now", BuyNow)
            .RequireAuthorization()
            .WithName("BuyNow")
            .WithTags("Bids")
            .WithSummary("Buy a vehicle outright")
            .WithDescription("No body: the vehicle is named in the address. Refused when the vehicle has no buy-now price, "
                + "is already sold, or its auction is not live.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        #region market-endpoints
        // The buyer's bids, each one answering the question the badge asks: am I still
        // winning this? The server owns that answer because it owns both sides of it.
        // Signed out, this is an empty map rather than a 401: the page asks for it on
        // every load, and "you have no bids" is the true answer for somebody who has
        // not signed in. The endpoints that change something are the ones that refuse.
        app.MapGet("/api/bids", MyBids)
            .WithName("GetMyBids")
            .WithTags("Bids")
            .WithSummary("The caller's standing on every vehicle they have bid on")
            .WithDescription("Keyed by vehicle id. Signed out, this is an empty map rather than a 401: the page asks for it "
                + "on every load, and no bids is the true answer for a visitor with no session.");

        #region history
        // The account page's list, newest first, with the vehicle each bid is on. The
        // only query the bids table serves that is not "load everything at startup";
        // the primary key leads with the user column, so it needs no index of its own.
        app.MapGet("/api/bids/history", History)
            .RequireAuthorization()
            .WithName("GetBidHistory")
            .WithTags("Bids")
            .WithSummary("The caller's bids, newest first, with the vehicle each is on")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        #endregion history

        // One round of bidding by the room, driven by the page rather than a timer. The room
        // bids on the server's clock, the same one every visitor is served, so the auctions it
        // can see are the ones the visitor sees.
        app.MapPost("/api/market/tick", Tick)
            .RequireAuthorization()
            .WithName("TickRoom")
            .WithTags("Bids")
            .WithSummary("One round of bidding by the room")
            .WithDescription("The simulated room bids against the caller's auctions and a page of live ones, on the server's "
                + "clock. Driven by the page rather than a timer, and only by a signed-in visitor, because moving the "
                + "room moves everybody's prices.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        #endregion market-endpoints

        app.MapDelete("/api/bids", ResetMine)
            .RequireAuthorization()
            .WithName("ResetMyBids")
            .WithTags("Bids")
            .WithSummary("Forget the caller's bids, and the room's answers to them")
            .WithDescription("One person's start-over. Nobody else's bids move.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        #endregion bid-endpoints

        return app;
    }

    private static Task<Results<Ok<BidResult>, ProblemHttpResult>> PlaceBid(CurrentBackend current, HttpContext http, string id, BidRequest request) =>
        HandleBid(current, http, id,
            (auction, vehicle, clock) => auction.PlaceBidAsync(vehicle, request.Amount, clock, http.UserId()));

    private static Task<Results<Ok<BidResult>, ProblemHttpResult>> BuyNow(CurrentBackend current, HttpContext http, string id) =>
        HandleBid(current, http, id,
            (auction, vehicle, clock) => auction.BuyNowAsync(vehicle, clock, http.UserId()));

    private static Ok<IReadOnlyDictionary<string, BidView>> MyBids(CurrentBackend current, HttpContext http) =>
        TypedResults.Ok(BidsOf(current.Auction, http));

    private static Ok<BidHistory> History(CurrentBackend current, HttpContext http)
    {
        var auction = current.Auction;
        var history = auction.BidsOf(http.UserId())
            .OrderByDescending(entry => entry.Value.AtMs)
            .Select(entry => new BidHistoryEntry(
                entry.Key,
                auction.Find(entry.Key) is { } v ? $"{v.Year} {v.Make} {v.Model}" : "(withdrawn)",
                entry.Value))
            .ToList();
        return TypedResults.Ok(new BidHistory(history.Count, history));
    }

    // The room's round, and the caller's own badges riding back with it, so a page does not need
    // a second request to find out it has been outbid.
    private static Ok<TickResult> Tick(CurrentBackend current, HttpContext http)
    {
        var auction = current.Auction;
        var raised = auction.RoomRound(Clocks.Now());
        return TypedResults.Ok(new TickResult(raised.Count, BidsOf(auction, http)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetMine(CurrentBackend current, HttpContext http)
    {
        if (http.UserIdOrNull() is not { } userId)
        {
            return TypedResults.Problem(detail: "Sign in to reset your bids.", statusCode: 401, title: "Not signed in");
        }
        await current.Auction.ResetAsync(userId);
        return TypedResults.NoContent();
    }

    /// <summary>The caller's badges, or an empty map for a visitor with no session: no bids is the true answer for them.</summary>
    private static IReadOnlyDictionary<string, BidView> BidsOf(Auction auction, HttpContext http) =>
        http.UserIdOrNull() is { } me ? auction.BidsOf(me) : new Dictionary<string, BidView>(StringComparer.Ordinal);

    #region bid-handling
    // One handler behind both bid endpoints, answering three questions in order: is this session
    // on this store, does the vehicle exist, does the domain accept the action on the server's
    // clock. The status codes are the contract the browser relies on: 401, 404, then 400.
    private static async Task<Results<Ok<BidResult>, ProblemHttpResult>> HandleBid(
        CurrentBackend current, HttpContext http, string id, Func<Auction, Vehicle, AuctionClock, Task<BidOutcome>> action)
    {
        string userId = http.UserId();
        // #region session-per-store
        // A session bids where its account is. The header can put one request on the other
        // store, and a bid there would be a row under an id that store has no account for. The
        // token names the store that opened it, so this check costs no lookup.
        if (!http.SessionIsOn(current.Backend))
        {
            return TypedResults.Problem(
                detail: $"This session was opened on another store. Sign in on {current.Backend.Name} to bid here.",
                statusCode: 401, title: "The bid was rejected");
        }
        // #endregion session-per-store
        var auction = current.Auction;
        var clock = Clocks.Now();
        if (auction.Find(id) is not { } vehicle)
        {
            return TypedResults.Problem(detail: "No vehicle has that id.", statusCode: 404, title: "No such vehicle");
        }
        var outcome = await action(auction, vehicle, clock);
        if (outcome.Kind == BidOutcomeKind.Rejected)
        {
            return TypedResults.Problem(detail: outcome.Reason, statusCode: 400, title: "The bid was rejected");
        }
        return TypedResults.Ok(
            new BidResult(
                outcome.Kind.ToString().ToLowerInvariant(),
                outcome.Amount,
                // The room's answer rides back with the bid, so the badge is right the moment the
                // response lands. TryGetValue rather than the indexer, because a reset from the
                // same account in a second tab can land between the bid and this read.
                auction.BidsOf(userId).TryGetValue(id, out var view) ? view : null,
                VehicleWire.ToWire(auction.AsItStands(vehicle), clock, auction.IsSold(vehicle.Id))));
    }
    #endregion bid-handling
}

/// <summary>
/// Bid submission: the amount, and nothing else. The schedule is the server's, so a request
/// never names a time.
/// </summary>
/// <param name="Amount">Whole dollars, at or above the vehicle's min_next_bid.</param>
public sealed record BidRequest([property: Description("Whole dollars, at or above the vehicle's min_next_bid.")] int Amount);
