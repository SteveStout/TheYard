import type { ReserveState, Vehicle } from './types';

export type { ReserveState };

/**
 * Client-side auction presentation logic. The API owns all auction math:
 * windows, status, minimum bids, reserve state and bid validation arrive on
 * the wire (auction_starts_at / auction_ends_at / auction_status /
 * min_next_bid / reserve_state). The browser's jobs are recomputing status
 * from the window as the clock ticks, re-ranking the page it holds when that
 * status moves (ADR: The listing that went stale), laying the buyer's own bid
 * over the figures (useBids.ts), and showing reserve state.
 */

export type AuctionStatus = 'upcoming' | 'live' | 'ended';

export interface AuctionTiming {
  /** Epoch ms. */
  startsAt: number;
  endsAt: number;
  status: AuctionStatus;
}

/** Rule: live from startsAt (inclusive) until endsAt (exclusive). */
export function auctionStatus(startsAt: number, endsAt: number, now: number): AuctionStatus {
  if (now < startsAt) return 'upcoming';
  if (now < endsAt) return 'live';
  return 'ended';
}

/**
 * A vehicle's window (from the server) with its status recomputed at `now`,
 * so countdowns hitting zero flip the status without waiting for a refetch.
 */
export function auctionTiming(vehicle: Vehicle, now: number): AuctionTiming {
  return {
    startsAt: vehicle.auction_starts_at,
    endsAt: vehicle.auction_ends_at,
    status: auctionStatus(vehicle.auction_starts_at, vehicle.auction_ends_at, now),
  };
}

/**
 * The next moment a listing on screen changes state by itself: the soonest
 * auction end or start still in the future, or null when nothing on this page
 * has a boundary left to cross.
 *
 * This exists because of what the front page looked like a minute after it
 * loaded (ADR: The listing that went stale while you looked at it). The default sort is ending soonest, the server ranks live auctions
 * ahead of ended ones, and the browser recomputes each card's status as the
 * clock ticks. So the first row is the closest to ending, those countdowns
 * reach zero while somebody is reading, and the cards turn into "Ended" chips
 * and stay exactly where the server put them, at the top. Nothing was wrong
 * with the ranking; it was answered once and never asked again.
 *
 * Asking again on a fixed timer would work and would ask constantly for nothing
 * on a page where the soonest auction ends tomorrow. The boundary is the moment
 * the answer can actually have changed, so it is the moment worth asking.
 */
export function nextAuctionBoundary(
  vehicles: readonly Pick<Vehicle, 'auction_starts_at' | 'auction_ends_at'>[],
  now: number
): number | null {
  let soonest: number | null = null;
  for (const vehicle of vehicles) {
    for (const moment of [vehicle.auction_starts_at, vehicle.auction_ends_at]) {
      if (moment > now && (soonest === null || moment < soonest)) soonest = moment;
    }
  }
  return soonest;
}

/**
 * The same ranking the API applies to an ending-soonest page, re-applied in the
 * browser to the page it already holds: live first and closest to ending, then
 * upcoming and closest to starting, then ended and most recently ended.
 *
 * Re-applied, not invented. `VehicleOrdering.EndingSoonestRank` on the server
 * is this function, with these bands, and the reason this one exists is that
 * the server answered it once with the clock it had. Over a hundred thousand
 * auctions the soonest one ends within a second, so the top of that page is
 * expired before it finishes painting, and asking again on a timer cannot fix
 * it: whatever the interval, the newest answer's first row is also about to
 * end.
 *
 * What the browser has that the response does not is the current time. So it
 * reorders the vehicles it was given and changes nothing else: no vehicle is
 * added, none is dropped, the count and the paging stay the server's, and a
 * page it did not rank this way, sorted by price or by bids, is left exactly as
 * it arrived.
 */
export function byAuctionUrgency<T extends Pick<Vehicle, 'auction_starts_at' | 'auction_ends_at'>>(
  vehicles: readonly T[],
  now: number
): T[] {
  return [...vehicles].sort((a, b) => urgencyRank(a, now) - urgencyRank(b, now));
}

/** Live auctions sort by when they end, and everything else sorts after them. */
function urgencyRank(
  vehicle: Pick<Vehicle, 'auction_starts_at' | 'auction_ends_at'>,
  now: number
): number {
  // The same two bands the server uses, wide enough that no epoch millisecond
  // can reach the next one.
  const upcomingBand = 1_000_000_000_000_000;
  const endedBand = 2_000_000_000_000_000;

  switch (auctionStatus(vehicle.auction_starts_at, vehicle.auction_ends_at, now)) {
    case 'live':
      return vehicle.auction_ends_at;
    case 'upcoming':
      return upcomingBand + vehicle.auction_starts_at;
    default:
      return endedBand - vehicle.auction_ends_at;
  }
}

// ---------------------------------------------------------------------------
// Reserve state
// ---------------------------------------------------------------------------

/** UI copy for each reserve state. The reserve amount itself is never shown. */
export const RESERVE_STATE_LABELS: Record<ReserveState, string> = {
  'no-reserve': 'No reserve',
  met: 'Reserve met',
  'not-met': 'Reserve not met',
};

/**
 * The reserve state the server worked out from the reserve and the standing
 * bid. The browser is never sent the reserve amount, so it cannot work the
 * state out itself: it shows the server's answer. After a bid the page takes
 * the vehicle the bid response carries, or refetches the listing, so the
 * state is the server's for the new standing bid.
 */
export function reserveState(vehicle: Vehicle): ReserveState {
  return vehicle.reserve_state;
}

/** The price a buyer competes against: the high bid, or the opening ask before any bids. */
export function currentPrice(vehicle: Vehicle): number {
  return vehicle.current_bid ?? vehicle.starting_bid;
}

// #region stale-minimum
/**
 * Whether the minimum next bid the page holds is out of date. The minimum is
 * domain math and only the server has it, so the browser cannot recompute it
 * when a competing bid arrives, but it can tell that the one it holds is
 * impossible: once a bid stands, the next minimum is that bid plus an
 * increment, so a minimum at or below the standing bid is a number the server
 * has already moved past. Before any bid stands the rule is the other way
 * round, the opening ask is the minimum by definition and equal to the price,
 * and that is not stale; reading it as stale left the first bid on every
 * untouched vehicle impossible to place.
 */
export function minimumIsStale(vehicle: Vehicle): boolean {
  return vehicle.current_bid !== null && vehicle.min_next_bid <= vehicle.current_bid;
}
// #endregion stale-minimum
