import { useCallback, useEffect, useState } from 'react';
import type { Vehicle } from '../lib/types';
import {
  buyNowRequest,
  fetchBids,
  placeBidRequest,
  resetBidsRequest,
  type BidMap,
  type BidRecord,
  type BidResult,
} from '../lib/data';

/** A vehicle with the buyer's own bid layered over the server's figures. */
export function applyBidRecord(vehicle: Vehicle, record: BidRecord | undefined): Vehicle {
  return record
    ? { ...vehicle, current_bid: record.amount, bid_count: record.bid_count }
    : vehicle;
}

/**
 * The buyer's bids, owned by the API (validation and state live server-side;
 * this hook relays actions and mirrors the bid map for card badges).
 * `onMutate` fires after any successful change so the caller can refetch.
 */
export function useBids(onMutate?: () => void) {
  const [bids, setBids] = useState<BidMap>({});

  useEffect(() => {
    const controller = new AbortController();
    fetchBids(controller.signal)
      .then(setBids)
      .catch(() => {});
    return () => controller.abort();
  }, []);

  const placeBid = useCallback(
    async (vehicle: Vehicle, amount: number): Promise<BidResult> => {
      const result = await placeBidRequest(vehicle.id, amount);
      if (result.bid) {
        const record = result.bid;
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
      setBids({});
      onMutate?.();
    } catch {
      // The API is unreachable — keep the current state visible.
    }
  }, [onMutate]);

  return { bids, placeBid, buyNow, resetBids };
}
