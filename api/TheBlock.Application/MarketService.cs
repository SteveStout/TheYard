using System.Collections.Concurrent;
using TheBlock.Data;
using TheBlock.Domain;

namespace TheBlock.Application;

/// <summary>One simulated competitor's standing on a vehicle.</summary>
public sealed record MarketBid(int Amount, int BidCount, long AtMs);

/// <summary>
/// The other bidders (ADR: Competing bidders). A demo with one anonymous buyer
/// has no way to lose: every bid is the high bid forever, the reserve badge
/// never changes hands, and "you have been outbid" is a state the code can
/// describe but never reach. This service is the room the buyer is bidding in.
///
/// It holds its own bids exactly the way <see cref="BidService"/> holds the
/// buyer's, as an overlay rather than a mutation, because the dataset is
/// shared and immutable and both of them have to compose without either one
/// owning the vehicle.
/// </summary>
public sealed class MarketService(int graceSeconds = MarketService.DefaultGraceSeconds)
{
    private readonly ConcurrentDictionary<string, MarketBid> _bids = new();

    /// <summary>
    /// How stale the buyer's bid must be before the room answers it. Without
    /// this the demo outbids you a second after every bid, which is not
    /// competition, it is a slot machine that always loses.
    ///
    /// Configurable because the browser test would otherwise spend twenty
    /// seconds waiting to see a lead change hands, and a suite people learn to
    /// skip is worse than a suite that proves less. The Playwright servers set
    /// Market:GraceSeconds to 0; nothing else does.
    /// </summary>
    public const int DefaultGraceSeconds = 20;

    public TimeSpan Grace { get; } = TimeSpan.FromSeconds(Math.Max(graceSeconds, 0));

    /// <summary>
    /// The room stops at twice the opening ask. A simulated competitor with no
    /// ceiling wins every auction eventually, and a demo the visitor cannot
    /// win is a worse demo than one with no competitors at all.
    /// </summary>
    public static int CeilingFor(Vehicle vehicle) => vehicle.StartingBid * 2;

    public bool IsEmpty => _bids.IsEmpty;

    public IReadOnlyDictionary<string, MarketBid> Snapshot() =>
        new Dictionary<string, MarketBid>(_bids);

    public MarketBid? For(string vehicleId) =>
        _bids.TryGetValue(vehicleId, out var bid) ? bid : null;

    // #region apply
    /// <summary>
    /// The room's bid layered over whatever the vehicle already shows, and only
    /// when it is higher. Order matters at the composition root: the buyer's
    /// overlay goes on first and this one second, so a competitor who has since
    /// gone higher is what the page displays, which is the whole point. Take
    /// the two overlays in the other order and the buyer would always appear to
    /// be winning.
    /// </summary>
    public Vehicle Apply(Vehicle vehicle) =>
        _bids.TryGetValue(vehicle.Id, out var bid) && bid.Amount > (vehicle.CurrentBid ?? 0)
            ? vehicle with { CurrentBid = bid.Amount, BidCount = Math.Max(vehicle.BidCount, bid.BidCount) }
            : vehicle;
    // #endregion apply

    // #region tick
    /// <summary>
    /// One round of bidding by the room. Answers the buyer first, because a
    /// competitor who never competes with you is scenery, then adds activity on
    /// vehicles nobody here is watching so the grid moves on its own.
    ///
    /// Everything it does goes through the same <see cref="BidRules"/> the
    /// buyer's bids go through. A simulated bidder that could place a bid the
    /// rules forbid would be a second, quieter implementation of the auction,
    /// and the whole architecture here exists to not have one of those.
    /// </summary>
    /// <returns>The ids it raised, for the caller to log or push.</returns>
    public IReadOnlyList<string> Tick(
        IReadOnlyList<Vehicle> candidates,
        IReadOnlyDictionary<string, BidState> buyerBids,
        AuctionClock clock,
        int maxBids = 3)
    {
        var raised = new List<string>();
        foreach (var vehicle in Order(candidates, buyerBids))
        {
            if (raised.Count >= maxBids)
            {
                break;
            }
            if (TryRaise(vehicle, buyerBids, clock))
            {
                raised.Add(vehicle.Id);
            }
        }
        return raised;
    }

    /// <summary>
    /// The buyer's leads first, oldest bid first, then everything else. Sorting
    /// by the bid's age rather than picking at random is what makes the outbid
    /// moment arrive in a predictable order instead of a lottery.
    /// </summary>
    private IEnumerable<Vehicle> Order(
        IReadOnlyList<Vehicle> candidates,
        IReadOnlyDictionary<string, BidState> buyerBids)
    {
        var contested = candidates
            .Where(v => buyerBids.ContainsKey(v.Id))
            .OrderBy(v => For(v.Id)?.AtMs ?? 0);
        // Shuffled, because the candidates arrive in a stable order and the
        // room takes the first few every round. Left in order, the same three
        // soonest-ending cars were raised on every tick and doubled in about
        // two minutes while the other thirty-seven never moved.
        var rest = candidates
            .Where(v => !buyerBids.ContainsKey(v.Id))
            .OrderBy(_ => Random.Shared.Next());
        return contested.Concat(rest);
    }

    private bool TryRaise(
        Vehicle vehicle,
        IReadOnlyDictionary<string, BidState> buyerBids,
        AuctionClock clock)
    {
        buyerBids.TryGetValue(vehicle.Id, out var buyer);
        // A vehicle the buyer bought outright is not for sale any more.
        if (buyer is { WonBuyNow: true })
        {
            return false;
        }
        // The grace period applies to every vehicle, not only the contested
        // ones: it is measured from the room's own last move here, or from the
        // buyer's if the room has not moved here yet. Applying it only to the
        // buyer's vehicles let the room raise an uncontested car on every
        // single tick, which is how one listing doubled in two minutes.
        long? since = For(vehicle.Id)?.AtMs ?? buyer?.AtMs;
        if (since is { } last && clock.NowMs - last < Grace.TotalMilliseconds)
        {
            return false;
        }

        // The price to beat is the highest of the three: the dataset's figure,
        // the buyer's bid and the room's own. Reading only its own would let
        // the room bid under the visitor and call it a raise.
        var standing = Apply(vehicle);
        if (buyer is { } lead && lead.Amount > (standing.CurrentBid ?? 0))
        {
            standing = standing with
            {
                CurrentBid = lead.Amount,
                BidCount = Math.Max(standing.BidCount, lead.BidCount),
            };
        }
        if (standing.CurrentBid is { } current && current >= CeilingFor(vehicle))
        {
            return false;
        }

        int next = BidRules.MinNextBid(standing);
        // Never buy the vehicle out from under the visitor: a bid at or above
        // buy-now would win outright, and the room is here to compete, not to
        // end the auction.
        if (vehicle.BuyNowPrice is { } buyNow && next >= buyNow)
        {
            return false;
        }
        if (BidRules.ResolveBid(standing, next, clock).Kind != BidOutcomeKind.Accepted)
        {
            return false;
        }

        // AddOrUpdate rather than an indexer: two tabs ticking at once both
        // read the same standing price and compute the same next bid, and a
        // plain write lets the older timestamp land second, which rewinds the
        // grace window and lets the room bid again immediately.
        var placed = new MarketBid(next, standing.BidCount + 1, clock.NowMs);
        _bids.AddOrUpdate(
            vehicle.Id,
            placed,
            (_, existing) => existing.Amount >= placed.Amount ? existing : placed);
        return true;
    }
    // #endregion tick

    public void Reset() => _bids.Clear();
}
