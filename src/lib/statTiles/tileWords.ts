/**
 * The words the tiles and the cards below them share: an uptime, a time in
 * milliseconds, the stretch the hour's figures cover, and a tile's whole
 * sentence. It is its own file so every place that says a time or a stretch
 * says it the same way, by calling the same function.
 */
import type { StatTile } from './tileTypes';

/** Uptime as the two largest non-zero units: "2d 5h", "3h 12m" or "7m". */
export function uptimeWords(totalSeconds: number): string {
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

/**
 * A time in words. The readings are stored as whole milliseconds, so 0 really
 * means "under a millisecond". Every place that shows a time uses this, so they
 * all say it the same way.
 */
export function millisecondsWords(ms: number): string {
  return ms === 0 ? 'under 1 ms' : `${ms.toLocaleString('en-US')} ms`;
}

/**
 * The stretch the hour's traffic figures cover, worded to follow "in the". The
 * request ring holds a number of requests, not an hour, so on a busy hour it
 * reaches back only some minutes, and `ringMinutes` says how many; null or
 * absent means the figures are the whole hour.
 */
export function ringStretch(ringMinutes: number | null | undefined): string {
  if (ringMinutes === null || ringMinutes === undefined) return 'last hour';
  return ringMinutes === 1 ? 'last minute' : `last ${ringMinutes} minutes`;
}

/** The words a stretch short of the hour needs after it, so nobody reads it as the hour cut off by mistake. */
export const RING_HOLDS = ', all the request ring holds';

/** The stretch in full: the short form, and the reason it is short when it is. */
export function ringStretchInFull(ringMinutes: number | null | undefined): string {
  return (
    ringStretch(ringMinutes) + (ringMinutes === null || ringMinutes === undefined ? '' : RING_HOLDS)
  );
}

/** A tile's whole sentence: the line under its number plus the part left out. */
export function tileSentence(tile: Pick<StatTile, 'detail' | 'more'>): string {
  return tile.detail + (tile.more ?? '');
}
