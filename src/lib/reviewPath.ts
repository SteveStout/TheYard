/**
 * Does:      Holds the review route's short path (ADR: The review route gives a reason before it asks, the addendum on
 *            the short path): its three stops' words, word for word from the design, where each one lands, the place
 *            kept on this browser under its own key, apart from the seven-stop tour's, and the pickers that turn the
 *            chart's readings and the live lot into what each tile prints.
 * Does not:  Read the API or draw anything; no React in here. Every number on a tile is read, never typed.
 * Used by:   useReviewPath.ts, ReviewPath.tsx, QualityTrendTile.tsx, GateTile.tsx, LotTile.tsx, reviewPath.test.ts.
 */
import { figures } from './landingProof';
import { parseTrend, type QualityTrendData } from './qualityTrend';
import type { ReviewAction, StorageLike } from './reviewProgress';
import { PROOF_ADDRESS } from './reviewVerdict';

/** One stop of the short path: its key, its words, its time and the action its button reports. */
export type PathStop = {
  key: 'quality' | 'gate' | 'room';
  title: string;
  time: string;
  payoff: string;
  tile: string;
  action: ReviewAction;
};

/** The three stops, word for word from the design, in the order the path draws them. */
export const PATH_STOPS: readonly PathStop[] = [
  {
    key: 'quality',
    title: 'Quality held with AI',
    time: '1 min',
    payoff:
      'Claude writes most of this code. It slipped for two weeks in October, a ratchet turned it, and each number since is only allowed to improve.',
    tile: 'One quality number, by version',
    action: 'stop-quality',
  },
  {
    key: 'gate',
    title: 'The gate every push passes',
    time: '30 sec',
    payoff:
      'Nothing ships until one gate passes: xUnit, Vitest, Playwright with axe and the architecture rules, on both stores.',
    tile: 'This version,',
    action: 'stop-gate',
  },
  {
    key: 'room',
    title: 'A system that is really running',
    time: '30 sec',
    payoff: 'vehicles filtered, sorted and paged by the store, in an auction room that bids back.',
    tile: 'A live lot, right now',
    action: 'stop-room',
  },
];

/** The words around the path: its button, the place line and the chart's three captions. */
export const PATH = {
  open: 'Open this stop',
  place: 'About 2 minutes. Your place is kept on this browser.',
  captions: ['October: the slip', 'the ratchet', 'every version since'],
  noLot: 'No auction is live right now',
  bidding: 'the room is bidding',
} as const;

/** The gate's six parts as the short path's tile words them; the last carries CodeRabbit's word. */
export const PATH_GATE_PARTS = [
  'xUnit',
  'Vitest',
  'Playwright with axe',
  'Architecture rules',
  'Azure SQL and Cosmos DB',
  'CodeRabbit',
] as const;

/** "0 of 3 stops", from the stops reached on this browser. */
export function pathProgress(reached: readonly string[]): string {
  return `${reached.length} of ${PATH_STOPS.length} stops`;
}

/** The key the short path's place is kept under: its own, apart from the tour's. */
export const PATH_KEY = 'theyard-review-short';

/** The stops reached on this browser, by key; none when nothing is kept, the store refuses, or what is kept is not a list. */
export function readPath(storage: StorageLike | null): string[] {
  if (storage === null) return [];
  try {
    const kept: unknown = JSON.parse(storage.getItem(PATH_KEY) ?? '[]');
    const keys = PATH_STOPS.map((stop) => stop.key as string);
    return Array.isArray(kept) ? keys.filter((key) => kept.includes(key)) : [];
  } catch {
    return [];
  }
}

/** Keeps a stop as reached; a store that refuses is left as it is, and the page goes on. */
export function markPath(
  storage: StorageLike | null,
  reached: readonly string[],
  key: string
): string[] {
  const next = PATH_STOPS.map((stop) => stop.key as string).filter(
    (each) => each === key || reached.includes(each)
  );
  if (storage !== null) {
    try {
      storage.setItem(PATH_KEY, JSON.stringify(next));
    } catch {
      // A private window or a full store: the place lasts as long as the page does.
    }
  }
  return next;
}

/** The chart's data: the readings the API serves under the Quality page's own label, direction and gold mark. */
export function pathTrend(
  readings: readonly string[] | null,
  marker: string
): QualityTrendData | null {
  if (readings === null || readings.length === 0) return null;
  return parseTrend(
    ['Async methods with no CancellationToken | may only fall | ' + marker, ...readings].join('\n')
  );
}

/** The short path's lot, as GET /api/review/lot answers it. */
export type PathLot = {
  live: boolean;
  vehicle_id: string | null;
  name: string | null;
  current_bid: number | null;
  ends_at_ms: number | null;
  total: number;
};

/** Where the room's button lands: the lot the tile shows, or the inventory when none is live. */
export function roomAddress(lot: PathLot | null): string {
  return lot?.live && lot.vehicle_id
    ? `/?vehicle=${encodeURIComponent(lot.vehicle_id)}`
    : '/?view=inventory';
}

/** Where each stop's button lands: Quality at the slip and the turn, the tests card, the room. */
export function stopAddress(key: PathStop['key'], lot: PathLot | null): string {
  if (key === 'quality') return PROOF_ADDRESS;
  if (key === 'gate') return '/?view=admin&card=tests';
  return roomAddress(lot);
}

/** The store's size in words, "100,000", or "" before the lot's answer. */
export function storeTotal(lot: PathLot | null): string {
  return lot === null ? '' : figures(lot.total);
}

/** The lot's bid in words, "$12,500", or "" when it has none. */
export function bidWords(lot: PathLot | null): string {
  return lot?.current_bid == null ? '' : `$${figures(lot.current_bid)}`;
}
