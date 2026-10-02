using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
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
        // ---------------------------------------------------------------------------
        // Bidding, validated server-side by the domain's BidRules.
        //
        // This used to say "single anonymous buyer; state lives in API memory (isolated
        // demo)", and every clause of it became false without the sentence changing. A
        // bid belongs to an account (ADR: Accounts and per-user bids) and lives in Azure
        // SQL Database (ADR: The SQL Server backend), so nothing in this region is
        // consequence-free and none of it is only the caller's to change. Two places
        // still read as though it were, and both are recorded: ADR: Reset is one
        // person's start-over, and ADR: The room needs an account too.
        // ---------------------------------------------------------------------------

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
        // Until 1.0.0.112 it carried the caller's clock anchor, and a page from then
        // that still sends one is not read (ADR: Three readers with no memory of the
        // project, the addendum on the clock).
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

        // One round of bidding by the room, driven by the page rather than a timer
        // (ADR-027). The room bids on the server's clock, the same one every visitor
        // is served, so the set it can see is the set the visitor sees; a page
        // that sent its own midnight here had a round from another zone bid on a
        // different set (ADR: Three readers with no memory of
        // the project, the addendum on the clock).
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
            // The room's standing price is what the minimum next bid is measured
            // against (ADR-027). Handing BidRules the dataset's figure instead
            // would let the buyer retake the lead with a bid below the going rate.
            (vehicle, clock) => current.Bids.PlaceBidAsync(current.Market.Apply(vehicle), request.Amount, clock, http.UserId()));

    private static Task<Results<Ok<BidResult>, ProblemHttpResult>> BuyNow(CurrentBackend current, HttpContext http, string id) =>
        HandleBid(current, http, id,
            (vehicle, clock) => current.Bids.BuyNowAsync(current.Market.Apply(vehicle), clock, http.UserId()));

    private static Ok<IReadOnlyDictionary<string, BidView>> MyBids(CurrentBackend current, HttpContext http) =>
        TypedResults.Ok(
            http.UserIdOrNull() is { } me
                ? BidViews.For(current.Bids, current.Market, me)
                : new Dictionary<string, BidView>(StringComparer.Ordinal));

    private static Ok<BidHistory> History(CurrentBackend current, HttpContext http)
    {
        var (inventory, bids, market) = current;
        var mine = BidViews.For(bids, market, http.UserId());
        var history = mine
            .OrderByDescending(entry => entry.Value.AtMs)
            .Select(entry => new BidHistoryEntry(
                entry.Key,
                inventory.GetById(entry.Key) is { } v ? $"{v.Year} {v.Make} {v.Model}" : "(withdrawn)",
                entry.Value))
            .ToList();
        return TypedResults.Ok(new BidHistory(history.Count, history));
    }

    private static Ok<TickResult> Tick(CurrentBackend current, HttpContext http)
    {
        var (inventory, bids, market) = current;
        var clock = Clocks.Now();
        // Everybody's high-water marks, not one account's. The room answers a
        // price rather than a person, and a room that only responded to whoever
        // happened to be looking would stop being a room the moment there were two
        // of them.
        //
        // That is still the right rule for what the room bids against. It is not a
        // reason for anybody at all to be allowed to advance it, which is what this
        // endpoint used to permit: no account, no cookie, and a loop of these
        // raises the price on every auction any signed-in visitor is winning, from
        // a stranger with curl, with nothing in the request ring to attribute it to
        // (ADR: The room needs an account too). Signing in is now the price of
        // moving the room, which is the same price as bidding.
        var buyerBids = bids.StandingAsBids();
        // Candidates: everything the buyer is in on, plus a page of live auctions
        // so the grid moves even when the visitor has bid on nothing.
        var contested = buyerBids.Keys
            .Select(inventory.GetById)
            .Where(v => v is not null)
            .Select(v => v!);
        // Take the first forty live auctions rather than searching for them.
        // Search would derive a status for all hundred thousand rows and then sort
        // the forty-odd thousand matches to keep forty of them, every eight
        // seconds, for every open tab. Nothing here needs the soonest-ending ones;
        // it needs forty live ones, and the room shuffles them anyway.
        var live = inventory.GetAll()
            .Where(v => AuctionSchedule.StatusFor(v.Id, clock) == AuctionStatus.Live)
            .Take(40);
        var candidates = contested.Concat(live).DistinctBy(v => v.Id).ToList();
        var raised = market.Tick(candidates, buyerBids, clock);
        return TypedResults.Ok(
            new TickResult(
                raised.Count,
                // The caller's own badges ride back with the tick, so a page does
                // not need a second request to find out it has been outbid. There
                // is always a caller now: the endpoint requires one.
                http.UserIdOrNull() is { } me
                    ? BidViews.For(bids, market, me)
                    : new Dictionary<string, BidView>(StringComparer.Ordinal)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetMine(CurrentBackend current, HttpContext http)
    {
        var (_, bids, market) = current;
        if (http.UserIdOrNull() is not { } userId)
        {
            return TypedResults.Problem(detail: "Sign in to reset your bids.", statusCode: 401, title: "Not signed in");
        }

        // The caller's bids, and the room's answers on the vehicles the caller
        // touched. The room resets with the buyer, because leaving its bids
        // standing would mean the reset button clears your side of an auction and
        // not the other one. What it no longer does is clear anybody else's: this
        // endpoint used to take no user at all (ADR: Reset is one person's
        // start-over).
        market.Forget(await bids.ResetAsync(userId));
        return TypedResults.NoContent();
    }

    #region bid-handling
    // One local function behind both bid endpoints, answering three questions in
    // order: is this session on this store, does the vehicle exist, does the
    // domain accept the action, on the server's clock. Until 1.0.0.112 the first
    // question was whether the caller's clock anchor was plausible; there is no
    // anchor to ask about now. The status codes are the contract the browser
    // relies on (ADR-023).
    private static async Task<Results<Ok<BidResult>, ProblemHttpResult>> HandleBid(
        CurrentBackend current, HttpContext http, string id, Func<Vehicle, AuctionClock, Task<BidOutcome>> action)
    {
        var (inventory, bids, market) = current;
        string userId = http.UserId();
        // #region session-per-store
        // A session bids where its account is. The header can put one request on
        // the other store, and a bid there would be a row under an id that store
        // has no account for: the relational store's foreign key answered that
        // with a 500, the document store with nothing. The token says which store
        // opened it, so this costs no lookup (ADR: Three readers with no memory of
        // the project).
        if (!http.SessionIsOn(current.Backend))
        {
            return TypedResults.Problem(
                detail: $"This session was opened on another store. Sign in on {current.Backend.Name} to bid here.",
                statusCode: 401, title: "The bid was rejected");
        }
        // #endregion session-per-store
        var clock = Clocks.Now();
        if (inventory.GetById(id) is not { } vehicle)
        {
            return TypedResults.Problem(detail: "No vehicle has that id.", statusCode: 404, title: "No such vehicle");
        }
        var outcome = await action(vehicle, clock);
        if (outcome.Kind == BidOutcomeKind.Rejected)
        {
            return TypedResults.Problem(detail: outcome.Reason, statusCode: 400, title: "The bid was rejected");
        }
        return TypedResults.Ok(
            new BidResult(
                outcome.Kind.ToString().ToLowerInvariant(),
                outcome.Amount,
                // The room's answer rides back with the bid, so the badge is right
                // the moment the response lands rather than at the next tick.
                // TryGetValue, not the indexer: a reset can land between the bid being
                // recorded and this line reading it back. That used to be any visitor's
                // reset, because DELETE /api/bids took no user at all; it is now only
                // this account's, from a second tab, which is rarer and just as real.
                BidViews.For(bids, market, userId).TryGetValue(id, out var view) ? view : null,
                VehicleWire.ToWire(market.Apply(bids.Apply(vehicle)), clock, bids.IsSold(vehicle.Id))));
    }
    #endregion bid-handling
}

/// <summary>
/// Bid submission: the amount, and nothing else. The clock anchor the page sent
/// until 1.0.0.112 is ignored if a page from then still sends it; the schedule
/// is the server's (ADR: Three readers with no memory of the project, the
/// addendum on the clock).
/// </summary>
/// <param name="Amount">Whole dollars, at or above the vehicle's min_next_bid.</param>
public sealed record BidRequest([property: Description("Whole dollars, at or above the vehicle's min_next_bid.")] int Amount);
