import type { Vehicle } from './types';

/**
 * Client-side auction presentation logic. The API owns all auction math —
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
