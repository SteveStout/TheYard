/**
 * Where a reviewer is on the route (ADR: Ten minutes, seven stops), kept on the browser and never
 * per code: the stop they are on and the stops they have reached, under two keys of the browser's
 * own storage. Pure functions over anything shaped like Storage, every read and every write in a
 * try, because a private window throws and the page must work without a place kept. No React in here.
 */
import { linkCode } from './links';
import { REVIEW_STOPS } from './reviewStops';

/** The key the current stop is kept under, its index from 0. */
export const CURRENT_KEY = 'theyard-review-stop';

/** The key the reached stops are kept under, a JSON list of indexes. */
export const REACHED_KEY = 'theyard-review-reached';

/** The two calls of Storage this file makes, so a test can hand it a map and the server hands it nothing. */
export type StorageLike = Pick<Storage, 'getItem' | 'setItem'>;

/** The place on the route: the stop showing "Do this", and the stops done, each an index from 0. */
export type ReviewProgress = { current: number; reached: number[] };

/** The index of the last stop. */
const LAST = REVIEW_STOPS.length - 1;

/** Stops in route order. */
const byIndex = (a: number, b: number) => a - b;

/** True when the number is the index of a stop. */
function isStop(index: unknown): index is number {
  return typeof index === 'number' && Number.isInteger(index) && index >= 0 && index <= LAST;
}

/** The place a reviewer has before they have done anything: the first stop, nothing reached. */
export function emptyProgress(): ReviewProgress {
  return { current: 0, reached: [] };
}

/** The kept place, or the first stop when nothing is kept, the keeping is refused, or what is kept is not a place. */
export function readProgress(storage: StorageLike | null): ReviewProgress {
  if (storage === null) return emptyProgress();
  try {
    const current = Number(storage.getItem(CURRENT_KEY) ?? '0');
    let reached: unknown = [];
    try {
      reached = JSON.parse(storage.getItem(REACHED_KEY) ?? '[]');
    } catch {
      reached = [];
    }
    const list = Array.isArray(reached) ? reached.filter(isStop) : [];
    return { current: isStop(current) ? current : 0, reached: [...new Set(list)].sort(byIndex) };
  } catch {
    return emptyProgress();
  }
}

/** Keeps the place; a browser that refuses is left as it is, and the page goes on. */
export function writeProgress(storage: StorageLike | null, progress: ReviewProgress): void {
  if (storage === null) return;
  try {
    storage.setItem(CURRENT_KEY, String(progress.current));
    storage.setItem(REACHED_KEY, JSON.stringify([...progress.reached].sort(byIndex)));
  } catch {
    // A private window or a full store: the place lasts as long as the page does.
  }
}

/** The place moved to a stop, held within the seven. */
export function moveTo(progress: ReviewProgress, index: number): ReviewProgress {
  return { ...progress, current: Math.min(LAST, Math.max(0, index)) };
}

/** The place with one more stop reached; the same place when it already was, or the index is no stop. */
export function markReached(progress: ReviewProgress, index: number): ReviewProgress {
  if (!isStop(index) || progress.reached.includes(index)) return progress;
  return { ...progress, reached: [...progress.reached, index].sort(byIndex) };
}

/** What the page sends to POST /api/review/reached: the stop (0 is opened, then 1 to 7), and the code only when it is one. */
export function reachBody(stop: number, code: string | null): { stop: number; r?: string } {
  const r = code === null ? null : linkCode(`?r=${encodeURIComponent(code)}`);
  return r === null ? { stop } : { stop, r };
}

/** The stops a move reached for the first time, numbered as the route numbers them (1 to 7), each reported once. */
export function newlyReached(before: ReviewProgress, after: ReviewProgress): number[] {
  return after.reached.filter((index) => !before.reached.includes(index)).map((index) => index + 1);
}
