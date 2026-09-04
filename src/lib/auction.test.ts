import { describe, expect, it } from 'vitest';
import {
  auctionStatus,
  auctionTiming,
  currentPrice,
  nextAuctionBoundary,
  reserveState,
} from './auction';
import type { Vehicle } from './types';

const DAY_MS = 24 * 60 * 60 * 1000;
const NOW = new Date('2026-08-15T12:00:00').getTime();

const baseVehicle: Vehicle = {
  id: '3cc3b89e-68b0-479e-af39-bca6251ea0b4',
  vin: 'TRD7L1KS0HNB5X3K3',
  year: 2023,
  make: 'Ford',
  model: 'Bronco',
  trim: 'Big Bend',
  body_style: 'SUV',
  exterior_color: 'Burgundy',
  interior_color: 'Beige',
  engine: '2.7L EcoBoost V6',
  transmission: 'automatic',
  drivetrain: '4WD',
  odometer_km: 47731,
  fuel_type: 'gasoline',
  condition_grade: 3.8,
  condition_report: 'Average condition.',
  damage_notes: ['Scratch on liftgate'],
  title_status: 'clean',
  province: 'Ontario',
  city: 'Toronto',
  auction_start: '2026-04-05T14:00:00',
  starting_bid: 14500,
  reserve_price: 25000,
  buy_now_price: null,
  images: ['/api/images/suv-01.jpg'],
  selling_dealership: 'King City Auto',
  lot: 'A-0043',
  current_bid: 22800,
  bid_count: 16,
  auction_starts_at: NOW - DAY_MS,
  auction_ends_at: NOW + DAY_MS,
  auction_status: 'live',
  min_next_bid: 23300,
};

const makeVehicle = (overrides: Partial<Vehicle> = {}): Vehicle => ({
  ...baseVehicle,
  ...overrides,
});

describe('auctionStatus', () => {
  it('is upcoming before the window, live inside it, ended after it', () => {
    const startsAt = NOW;
    const endsAt = NOW + DAY_MS;
    expect(auctionStatus(startsAt, endsAt, startsAt - 1)).toBe('upcoming');
    expect(auctionStatus(startsAt, endsAt, startsAt)).toBe('live');
    expect(auctionStatus(startsAt, endsAt, endsAt - 1)).toBe('live');
    expect(auctionStatus(startsAt, endsAt, endsAt)).toBe('ended');
  });
});

describe('auctionTiming', () => {
  it('recomputes status from the server window as the clock advances', () => {
    const vehicle = makeVehicle(); // server said "live" at fetch time
    expect(auctionTiming(vehicle, NOW).status).toBe('live');
    // Two days later the same payload reads as ended without a refetch.
    expect(auctionTiming(vehicle, NOW + 2 * DAY_MS).status).toBe('ended');
  });
});

describe('reserveState', () => {
  it('is "no-reserve" when reserve_price is null', () => {
    expect(reserveState(makeVehicle({ reserve_price: null }))).toBe('no-reserve');
  });

  it('is "met" when the high bid reaches the reserve, including exactly', () => {
    expect(reserveState(makeVehicle({ current_bid: 25000 }))).toBe('met');
    expect(reserveState(makeVehicle({ current_bid: 26000 }))).toBe('met');
  });

  it('is "not-met" below the reserve or before any bids exist', () => {
    expect(reserveState(makeVehicle({ current_bid: 22800 }))).toBe('not-met');
    expect(reserveState(makeVehicle({ current_bid: null, bid_count: 0 }))).toBe('not-met');
  });
});

describe('currentPrice', () => {
  it('is the high bid, or the opening ask before any bids', () => {
    expect(currentPrice(makeVehicle({ current_bid: 22800 }))).toBe(22800);
    expect(currentPrice(makeVehicle({ current_bid: null }))).toBe(14500);
  });
});

describe('nextAuctionBoundary', () => {
  /**
   * The front page is answered once and then read for minutes. This is the
   * moment its answer can have changed, and it is the reason a card that ended
   * while somebody was looking does not sit at the top of the list until they
   * reload.
   */
  const at = (startsAt: number, endsAt: number) =>
    makeVehicle({ auction_starts_at: startsAt, auction_ends_at: endsAt });

  it('is the soonest moment still ahead of now', () => {
    const vehicles = [
      at(NOW - DAY_MS, NOW + 90_000),
      at(NOW - DAY_MS, NOW + 30_000),
      at(NOW - DAY_MS, NOW + 60_000),
    ];
    expect(nextAuctionBoundary(vehicles, NOW)).toBe(NOW + 30_000);
  });

  it('counts a start as well as an end, because an upcoming lot going live changes the list too', () => {
    const vehicles = [at(NOW - DAY_MS, NOW + 60_000), at(NOW + 10_000, NOW + DAY_MS)];
    expect(nextAuctionBoundary(vehicles, NOW)).toBe(NOW + 10_000);
  });

  it('ignores moments that have already passed', () => {
    const vehicles = [at(NOW - DAY_MS, NOW - 1), at(NOW - DAY_MS, NOW + 5_000)];
    expect(nextAuctionBoundary(vehicles, NOW)).toBe(NOW + 5_000);
  });

  it('is null when nothing on the page has a boundary left, so nothing is asked for nothing', () => {
    expect(nextAuctionBoundary([at(NOW - DAY_MS, NOW - 1_000)], NOW)).toBeNull();
    expect(nextAuctionBoundary([], NOW)).toBeNull();
  });

  it('treats the boundary as passed at the instant it arrives, the way the status rule does', () => {
    // auctionStatus is live until endsAt exclusive, so at exactly endsAt the
    // card has already flipped and there is nothing left to wait for.
    expect(auctionStatus(NOW - DAY_MS, NOW, NOW)).toBe('ended');
    expect(nextAuctionBoundary([at(NOW - DAY_MS, NOW)], NOW)).toBeNull();
  });
});
