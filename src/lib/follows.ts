/**
 * Memory against traffic (ADR: Memory follows traffic): the wire GET /api/admin/follows answers,
 * and the arithmetic the card's two scatter plots draw from. The rule itself, whether memory
 * climbed while traffic stayed flat, is the server's (MemoryTraffic.cs); this file only places
 * points. No React in here.
 */

/** One point: a minute, or a kept bucket of minutes. */
export type FollowsPoint = {
  at: string;
  requests_per_minute: number;
  cpu_percent: number | null;
  working_set_mb: number;
};

/** The server's finding, in the tones every Cloud card uses. */
export type FollowsVerdict = {
  tone: 'good' | 'info' | 'warning' | 'alarm';
  words: string;
  memory_from_mb: number;
  memory_to_mb: number;
  requests_from: number;
  requests_to: number;
  climb_threshold_mb: number;
  cpu_follows_traffic: number | null;
  memory_follows_traffic: number | null;
};

/** The whole answer. */
export type FollowsReport = {
  window: string;
  as_of: string;
  bucket_minutes: number;
  memory_limit_mb: number;
  skipped_warm_up: boolean;
  points: FollowsPoint[];
  verdict: FollowsVerdict;
};

/** A placed dot, in the plot's own units from 0 to 1, with the reading it stands for. */
export type Dot = { x: number; y: number; requests: number; value: number; at: string };

/** One scatter: its dots, the top of each axis, and the straight line through the dots, if there is one. */
export type Scatter = {
  dots: Dot[];
  xMax: number;
  yMax: number;
  /** The least-squares line, as its two ends in plot units, or null with fewer than two distinct x. */
  trend: { x1: number; y1: number; x2: number; y2: number } | null;
};

/** A top for an axis that is a round number at or above the largest reading, never zero. */
export function roundTop(largest: number): number {
  if (!(largest > 0)) return 1;
  const step = 10 ** Math.floor(Math.log10(largest));
  for (const multiple of [1, 2, 2.5, 5, 10]) {
    if (multiple * step >= largest) return multiple * step;
  }
  return 10 * step;
}

/**
 * One scatter: requests a minute along the bottom, the chosen reading up the side. A reading
 * that was not taken is left out, never drawn as zero. `yTop` fixes the side's top (a percentage
 * runs to 100, memory to its limit) so a flat line looks flat.
 */
export function scatter(
  points: FollowsPoint[],
  pick: (point: FollowsPoint) => number | null,
  yTop?: number
): Scatter {
  const read = points
    .map((point) => ({ point, value: pick(point) }))
    .filter((entry): entry is { point: FollowsPoint; value: number } => entry.value !== null);
  const xMax = roundTop(Math.max(0, ...read.map((entry) => entry.point.requests_per_minute)));
  const yMax = yTop ?? roundTop(Math.max(0, ...read.map((entry) => entry.value)));
  const dots = read.map((entry) => ({
    x: entry.point.requests_per_minute / xMax,
    y: Math.min(1, entry.value / yMax),
    requests: entry.point.requests_per_minute,
    value: entry.value,
    at: entry.point.at,
  }));
  return { dots, xMax, yMax, trend: trendOf(dots) };
}

/** The least-squares line through the dots, clipped to the plot, or null when x never moves. */
export function trendOf(dots: Dot[]): Scatter['trend'] {
  if (dots.length < 2) return null;
  const meanX = dots.reduce((sum, dot) => sum + dot.x, 0) / dots.length;
  const meanY = dots.reduce((sum, dot) => sum + dot.y, 0) / dots.length;
  let top = 0;
  let bottom = 0;
  for (const dot of dots) {
    top += (dot.x - meanX) * (dot.y - meanY);
    bottom += (dot.x - meanX) * (dot.x - meanX);
  }
  if (bottom === 0) return null;
  const slope = top / bottom;
  const at = (x: number) => Math.max(0, Math.min(1, meanY + slope * (x - meanX)));
  const x1 = Math.min(...dots.map((dot) => dot.x));
  const x2 = Math.max(...dots.map((dot) => dot.x));
  return { x1, y1: at(x1), x2, y2: at(x2) };
}

/** A correlation in words a reader can check against the plot. */
export function followsWords(r: number | null): string {
  if (r === null) return 'did not move enough to compare';
  const strength = Math.abs(r) >= 0.7 ? 'closely' : Math.abs(r) >= 0.4 ? 'loosely' : 'hardly';
  return `${r < 0 && Math.abs(r) >= 0.4 ? 'moved against it' : `followed it ${strength}`} (r = ${r.toFixed(2)})`;
}
