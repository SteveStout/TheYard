import { useCallback, useEffect, useRef, useState } from 'react';
import type { Vehicle } from '../lib/types';
import {
  buyNowRequest,
  fetchBids,
  placeBidRequest,
  resetBidsRequest,
  tickMarket,
  type BidMap,
  type BidRecord,
  type BidResult,
} from '../lib/data';

/** How often the page asks the simulated room to bid (ADR-027). */
const MARKET_TICK_MS = 8_000;

/**
 * A vehicle with the buyer's own bid layered over the server's figures, and
 * the room's over that when the room is ahead (ADR-027). The order is the same
 * one the API composes its overlays in, for the same reason: showing the
 * buyer's figure over a higher competing bid would be showing them winning an
 * auction they are losing.
 */
export function applyBidRecord(vehicle: Vehicle, record: BidRecord | undefined): Vehicle {
  if (!record) {
    return vehicle;
  }
  // != null, not !== null: an API mid-roll can answer outbid without the
  // amount, and undefined slipping through here becomes current_bid:
  // undefined, which reads as "reserve not met" on a vehicle whose reserve is
  // met (reserveState tests for null, and undefined is not null).
  const winning =
    record.outbid && record.market_amount != null ? record.market_amount : record.amount;
  return { ...vehicle, current_bid: winning, bid_count: record.bid_count };
}

/**
 * The buyer's bids, owned by the API (validation and state live server-side;
 * this hook relays actions and mirrors the bid map for card badges).
 * `onMutate` fires after any successful change so the caller can refetch.
 */
export function useBids(onMutate?: () => void) {
  const [bids, setBids] = useState<BidMap>({});
  // #region tick-ordering
  // Everything the buyer does bumps this. A tick response carries the number
  // that was current when it was sent, and is thrown away if the buyer has
  // done anything since: the tick replaces the whole map, so a slow one
  // landing after a bid would erase a bid the server already accepted, and one
  // landing after Reset would put the cleared bids back. `inFlight` keeps a
  // second round from starting while the first is out, which also stops two
  // slow responses from applying out of order.
  const mutations = useRef(0);
  const inFlight = useRef(false);
  const applyMine = useCallback((next: BidMap) => {
    mutations.current += 1;
    setBids(next);
  }, []);
  // #endregion tick-ordering

  useEffect(() => {
    const controller = new AbortController();
    fetchBids(controller.signal)
      .then(setBids)
      .catch(() => {});
    return () => controller.abort();
  }, []);

  // #region market-loop
  // The room bids while the tab is open and not otherwise (ADR-027). A hidden
  // tab is a visitor who is not watching, and an auction house that runs on an
  // empty room is a background job this demo does not need to pay for.
  // onMutate fires only when something actually moved, so a quiet round costs
  // one small request and no rerender of the grid.
  useEffect(() => {
    let cancelled = false;
    const id = window.setInterval(() => {
      if (document.hidden || inFlight.current) return;
      inFlight.current = true;
      const sentAt = mutations.current;
      void tickMarket().then((result) => {
        inFlight.current = false;
        if (cancelled || !result) return;
        // The buyer bid or reset while this was out. Their action is newer
        // than this answer, so this answer is discarded.
        if (mutations.current !== sentAt) return;
        setBids(result.bids);
        if (result.raised > 0) onMutate?.();
      });
    }, MARKET_TICK_MS);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, [onMutate]);
  // #endregion market-loop

  const placeBid = useCallback(
    async (vehicle: Vehicle, amount: number): Promise<BidResult> => {
      const result = await placeBidRequest(vehicle.id, amount);
      if (result.bid) {
        const record = result.bid;
        mutations.current += 1;
        setBids((prev) => ({ ...prev, [vehicle.id]: record }));
        onMutate?.();
      }
      return result;
    },
    [onMutate]
  );

  const buyNow = useCallback(
    async (vehicle: Vehicle): Promise<BidResult> => {
      const result = await buyNowRequest(vehicle.id);
      if (result.bid) {
        const record = result.bid;
        mutations.current += 1;
        setBids((prev) => ({ ...prev, [vehicle.id]: record }));
        onMutate?.();
      }
      return result;
    },
    [onMutate]
  );

  const resetBids = useCallback(async () => {
    try {
      await resetBidsRequest();
      applyMine({});
      onMutate?.();
    } catch {
      // The API is unreachable, so keep the current state visible.
    }
  }, [onMutate, applyMine]);

  return { bids, placeBid, buyNow, resetBids };
}
