import type { Vehicle } from './types';

/**
 * Client-side auction presentation logic. The API owns all auction math:
 * windows, status, minimum bids, and bid validation arrive on the wire
 * (auction_starts_at / auction_ends_at / auction_status / min_next_bid).
 * The browser's only jobs are recomputing status from the window as the
 * clock ticks, and showing reserve state.
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

// ---------------------------------------------------------------------------
// Reserve state
// ---------------------------------------------------------------------------

export type ReserveState = 'no-reserve' | 'met' | 'not-met';

/** UI copy for each reserve state. The reserve amount itself is never shown. */
export const RESERVE_STATE_LABELS: Record<ReserveState, string> = {
  'no-reserve': 'No reserve',
  met: 'Reserve met',
  'not-met': 'Reserve not met',
};

/**
 * Rule: a null reserve_price means the vehicle sells at any price; otherwise
 * the reserve is met once the high bid reaches it. With no bids yet
 * (current_bid === null) a reserve cannot be met.
 */
export function reserveState(vehicle: Vehicle): ReserveState {
  if (vehicle.reserve_price === null) return 'no-reserve';
  if (vehicle.current_bid !== null && vehicle.current_bid >= vehicle.reserve_price) return 'met';
  return 'not-met';
}

/** The price a buyer competes against: the high bid, or the opening ask before any bids. */
export function currentPrice(vehicle: Vehicle): number {
  return vehicle.current_bid ?? vehicle.starting_bid;
}
