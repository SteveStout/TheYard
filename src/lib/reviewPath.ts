/**
 * Does:      Holds the review route's short path (ADR: The review route gives a reason before it asks, the addendum on
 *            the short path): its three stops' words, word for word from the design, where each one lands, the place
 *            kept on this browser under its own key, apart from the seven-stop tour's, and the pickers that turn the
 *            chart's readings into the tile's spark and the live lot into its name, its bid and its countdown.
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

/** The words around the path: its heading, its place, its line, its button and the tiles' own words. */
export const PATH = {
  title: 'Two minutes, three stops',
  place: 'Your place is kept on this browser.',
  hint: 'Each tile is the running system, read live. Open one and you are inside it.',
  open: 'Open this stop',
  captions: ['October: the slip', 'the ratchet', 'every version since'],
  noLot: 'No auction is live right now',
  bidding: 'the room is bidding',
  currentBid: 'Current bid',
  openingBid: 'Opening bid',
  closes: 'Closes in',
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

/** "0 of 3 opened.", from the stops reached on this browser. */
export function pathProgress(reached: readonly string[]): string {
  return `${reached.length} of ${PATH_STOPS.length} opened.`;
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

/** The short path's lot, as GET /api/review/lot answers it: the room has taken its turn on it before the answer. */
export type PathLot = {
  live: boolean;
  vehicle_id: string | null;
  name: string | null;
  current_bid: number | null;
  opening_bid: number | null;
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

/**
 * What the lot tile prints under the name: the bid with the room bidding, or, when the room's rules refused its turn
 * and nobody has bid, the opening ask with no dots; nothing before the answer.
 */
export function lotAmount(lot: PathLot | null): {
  label: string;
  amount: string;
  bidding: boolean;
} {
  if (lot?.current_bid != null)
    return { label: PATH.currentBid, amount: bidWords(lot), bidding: true };
  if (lot?.opening_bid != null)
    return { label: PATH.openingBid, amount: `$${figures(lot.opening_bid)}`, bidding: false };
  return { label: PATH.currentBid, amount: '', bidding: false };
}

/** The time a lot has left as "m:ss", counted on the page's clock; "0:00" once it has closed. */
export function closesIn(endsAtMs: number, nowMs: number): string {
  const seconds = Math.max(0, Math.floor((endsAtMs - nowMs) / 1000));
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`;
}

/** The spark's drawing box: the tile's own units, stretched to its width (preserveAspectRatio none). */
export const SPARK = { width: 360, height: 84, inset: 6, top: 6, bottom: 78, floor: 82 } as const;

/**
 * The quality chart at tile size, as the picture draws it: the readings as one plain line across the tile, the area
 * under it up to the ratchet's version filled, and the ratchet's x for its dashed mark. Each reading sits at its day,
 * so the two weeks of the slip read as two weeks beside the month since, however many versions a day shipped; higher
 * is worse, so the history's highest reading is at the top. Null before the readings, or when there are fewer than two.
 */
export function sparkGeometry(
  readings: readonly string[] | null,
  marker: string
): { before: string; after: string; area: string; markX: number } | null {
  const data = pathTrend(readings, marker);
  if (data === null || data.points.length < 2) return null;
  const values = data.points.map((point) => point.value);
  const high = Math.max(...values);
  const low = Math.min(...values);
  const days = data.points.map((point) => Date.parse(point.date));
  const first = days[0];
  const span = days[days.length - 1] - first;
  const at = values.map((value, index) => ({
    x:
      Math.round(
        (SPARK.inset +
          (span === 0 ? index / (values.length - 1) : (days[index] - first) / span) *
            (SPARK.width - 2 * SPARK.inset)) *
          10
      ) / 10,
    y:
      Math.round(
        (high === low
          ? SPARK.bottom
          : SPARK.top + ((high - value) / (high - low)) * (SPARK.bottom - SPARK.top)) * 10
      ) / 10,
  }));
  const found = data.points.findIndex((point) => point.version === marker);
  const mark = found < 0 ? at.length - 1 : found;
  const line = (points: typeof at) =>
    points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x} ${point.y}`).join(' ');
  const upTo = at.slice(0, mark + 1);
  return {
    before: line(upTo),
    after: line(at.slice(mark)),
    area: `${line(upTo)} L${at[mark].x} ${SPARK.floor} L${at[0].x} ${SPARK.floor} Z`,
    markX: at[mark].x,
  };
}
