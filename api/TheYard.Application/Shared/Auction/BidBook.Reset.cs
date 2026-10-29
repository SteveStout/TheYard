// One person's start-over: their bids go, and every vehicle they touched has its standing
// recomputed from whoever is left. It has a file of its own because it is the one write that
// removes facts rather than adding them, and the reasoning behind it is longer than the code
// (the type itself is in BidBook.cs).
namespace TheYard.Application.Shared.Auction;

/// <summary>The reset: one buyer's bids removed, and nobody else's touched.</summary>
public sealed partial class BidBook
{
    // #region reset
    /// <summary>
    /// One person's start-over: the caller's bids are removed and nobody
    /// else's. A reset that cleared everybody would let any signed in visitor
    /// delete every other visitor's rows, on a site whose own changelog says
    /// two visitors can outbid each other and both be told the truth (ADR:
    /// Reset is one person's start-over).
    ///
    /// <para>Each vehicle the caller touched gets its standing recomputed from
    /// whoever is left rather than deleted, so a stranger who bid on the same
    /// car keeps their bid and keeps the lead they earned.</para>
    ///
    /// <para>Returns the vehicles that have nobody bidding on them any more,
    /// which the caller passes to the room, because a reset that took away
    /// your bid while leaving the room's counter-bid standing reads as a bug.
    /// Only those vehicles, and not every vehicle the caller touched: "vehicles
    /// this person bid on" is not "vehicles only this person bid on", and
    /// clearing the room's answer on a shared car would take away a stranger's
    /// outbid badge and drop the price a stranger is competing at. The room is
    /// a separate service and this one does not reach into it.</para>
    ///
    /// <para>Cancellation stops the wait for the gate and nothing after it. The rows are
    /// dropped and then each touched vehicle is read again to see who is left; a reset
    /// abandoned between those would return no orphans and leave the room answering a price
    /// nobody is bidding (ADR: The ports learn to wait, the addendum on the token).</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> ResetAsync(string userId, CancellationToken cancellation)
    {
        await LoadAsync(cancellation);
        await _gate.WaitAsync(cancellation);
        try
        {
            // The store is asked even when this process holds nothing for the
            // caller, because the caller may have bid on the other site.
            string[] touched = _ledger.TouchedBy(userId);
            var orphaned = new List<string>();
            // The store first, then memory, the same rule the bid path follows.
            // Clearing the dictionaries before the store is asked would leave a
            // caller told their bids were gone, when a store that refused kept
            // the rows to be replayed at the next start (ADR: Three readers with
            // no memory of the project). The store drops the standing of every
            // vehicle this person touched along with their rows.
            await _store.ClearAsync(userId, CancellationToken.None);
            _ledger.Forget(userId);

            foreach (string vehicleId in touched)
            {
                // Recomputed, not removed, and from the store's rows rather than
                // this process's memory, so a bid placed on the other site
                // still counts. Removing it would hand the vehicle back to its
                // opening ask and delete a third person's bid without a word.
                var (held, _) = await CurrentAsync(vehicleId, CancellationToken.None);
                if (held is null)
                {
                    // Nobody left on this one, so the room's answer to it has
                    // nothing to be an answer to. These are the only vehicles
                    // the caller gets to clear the room on.
                    orphaned.Add(vehicleId);
                }
            }

            return orphaned;
        }
        finally
        {
            _gate.Release();
        }
    }
    // #endregion reset
}
