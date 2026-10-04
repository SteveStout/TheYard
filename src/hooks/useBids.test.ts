import { describe, expect, it } from 'vitest';
import type { BidRecord } from '../lib/data';
import type { Vehicle } from '../lib/types';
import { applyBidRecord } from './useBids';

const vehicle = { id: 'v-1', current_bid: 9_000, bid_count: 3 } as unknown as Vehicle;

const record = (overrides: Partial<BidRecord>): BidRecord => ({
  amount: 10_000,
  bid_count: 4,
  won_buy_now: false,
  at_ms: 1_700_000_000_000,
  outbid: false,
  market_amount: null,
  highest_amount: 10_000,
  ...overrides,
});

describe('applyBidRecord', () => {
  it('leaves a vehicle alone when the buyer has no bid on it', () => {
    expect(applyBidRecord(vehicle, undefined)).toBe(vehicle);
  });

  it("shows the buyer's own bid while it stands", () => {
    expect(applyBidRecord(vehicle, record({})).current_bid).toBe(10_000);
  });

  it("shows the room's bid when the room went higher", () => {
    const outbid = record({ outbid: true, market_amount: 11_000, highest_amount: 11_000 });
    expect(applyBidRecord(vehicle, outbid).current_bid).toBe(11_000);
  });

  it("shows the other buyer's bid when another account went higher and the room is silent", () => {
    const outbid = record({ outbid: true, market_amount: null, highest_amount: 12_500 });
    expect(applyBidRecord(vehicle, outbid).current_bid).toBe(12_500);
  });

  it("never lowers the price to the buyer's own figure when outbid", () => {
    const outbid = record({ outbid: true, market_amount: 10_500, highest_amount: 13_000 });
    expect(applyBidRecord(vehicle, outbid).current_bid).toBe(13_000);
  });
});
