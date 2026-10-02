/**
 * The hour's median and 95th percentile, worked out by pooling every request
 * time in the hour and taking the nearest rank. It is its own file because it
 * is the one piece of arithmetic the tiles must agree on with the API, and the
 * documents quote this region live.
 */

// #region hour-timing
/**
 * The hour's median and 95th percentile, over every request in the minutes
 * passed in (usually the warm ones from afterColdStart). You cannot get an
 * hour's percentile from its minutes' percentiles, so this pools every request
 * duration first. It uses the nearest-rank method, the same arithmetic as the
 * API's Percentiles.Of, so the two agree on an hour that has one minute.
 */
export function hourTiming(
  slots: { at: string; requests?: number | null; durations_ms?: number[] }[]
): {
  requests: number;
  p50_ms: number | null;
  p95_ms: number | null;
  /** The minute the slowest request was in, or null on an hour with none. */
  slowest_at: string | null;
} {
  const all: number[] = [];
  let slowest: number | null = null;
  let slowestAt: string | null = null;
  for (const slot of slots) {
    const durations = slot.durations_ms ?? [];
    for (const ms of durations) {
      all.push(ms);
      if (slowest === null || ms > slowest) {
        slowest = ms;
        slowestAt = slot.at;
      }
    }
  }
  all.sort((a, b) => a - b);
  // Nearest rank: the value at position ceil(p% of n), counting from 1.
  const rank = (percentile: number) =>
    all.length === 0 ? null : all[Math.max(0, Math.ceil((percentile / 100) * all.length) - 1)];
  return { requests: all.length, p50_ms: rank(50), p95_ms: rank(95), slowest_at: slowestAt };
}
// #endregion hour-timing
