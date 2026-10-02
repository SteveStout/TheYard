// The activity chart's geometry: the drawing area, the lines and fills as SVG
// paths, the three kinds stacked into bands, where a band's name goes, the
// part-day at the right end, and the x labels. Pure arithmetic on numbers, so
// it is tested without a browser, and its own file because the card only draws.

import type { PlotBox } from './plotFrame';
import type {
  ActivityDay,
  ActivityPoint,
  ActivitySeries,
  ActivityWho,
  ActivityWindow,
} from './activityTypes';

// #region chart-geometry
/**
 * The drawing area the lines are laid into, in SVG units: the desk's, which the
 * card scales up to its width, and narrowed to the width a phone gives it
 * (fitBox in src/lib/plotFrame.ts) so its words keep their size.
 */
export const CHART = { width: 720, height: 200, left: 36, right: 12, top: 12, bottom: 28 } as const;

/** The highest count on any series, or 1, so a flat day still has a y axis. */
export function ceilingOf(series: ActivitySeries[]): number {
  let top = 0;
  for (const line of series) {
    for (const point of line.points) {
      if (point.requests > top) top = point.requests;
    }
  }
  return top === 0 ? 1 : top;
}

/**
 * One series as the `d` of an SVG path: a polyline through every point, left
 * to right, scaled to the drawing area. Points sit at equal x steps because
 * the server hands back a fixed grid with zeros where nothing happened, so
 * the two stores share an axis without the page having to align them.
 */
export function linePath(points: ActivityPoint[], ceiling: number, box: PlotBox = CHART): string {
  if (points.length === 0) return '';
  const innerWidth = box.width - box.left - box.right;
  const innerHeight = box.height - box.top - box.bottom;
  const step = points.length === 1 ? 0 : innerWidth / (points.length - 1);
  return points
    .map((point, index) => {
      const x = box.left + index * step;
      const y = box.top + innerHeight - (point.requests / ceiling) * innerHeight;
      return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(' ');
}

/** The same line closed down to the baseline, for the soft fill under it. */
export function areaPath(points: ActivityPoint[], ceiling: number, box: PlotBox = CHART): string {
  const line = linePath(points, ceiling, box);
  if (line === '') return '';
  const innerWidth = box.width - box.left - box.right;
  const baseline = box.height - box.bottom;
  const step = points.length === 1 ? 0 : innerWidth / (points.length - 1);
  const lastX = box.left + (points.length - 1) * step;
  return `${line} L${lastX.toFixed(1)} ${baseline} L${box.left} ${baseline} Z`;
}

// #region stack
/**
 * The three kinds of traffic stacked: people at the bottom, then scanners and
 * crawlers, then the site's own reads, in that order always, so a colour means
 * the same kind on every window. Under Visitors only the stack is the people
 * alone. A band is the day-by-day floor and ceiling it fills between, in
 * visitor-days.
 */
export type ActivityKind = 'people' | 'scanners' | 'self';

/** The three kinds in stacking order, bottom first. */
export const ACTIVITY_KINDS: readonly ActivityKind[] = ['people', 'scanners', 'self'];

/** Each kind as the legend and the table name it. */
export const KIND_NAMES: Readonly<Record<ActivityKind, string>> = {
  people: 'People',
  scanners: 'Scanners and crawlers',
  self: "The site's own reads",
};

/** One kind's band: its floor and its ceiling on each day, in visitor-days. */
export type ActivityBand = { kind: ActivityKind; lower: number[]; upper: number[] };

/** The bands for the toggle, each one starting where the one below it ends, day by day. */
export function stackBands(days: ActivityDay[], who: ActivityWho): ActivityBand[] {
  const kinds: readonly ActivityKind[] = who === 'people' ? ['people'] : ACTIVITY_KINDS;
  const running = days.map(() => 0);
  return kinds.map((kind) => {
    const lower = [...running];
    days.forEach((day, index) => {
      running[index] += day[kind];
    });
    return { kind, lower, upper: [...running] };
  });
}

/** The top of the stack on its highest day, or 1, so a quiet window still has an axis. */
export function stackCeiling(bands: ActivityBand[]): number {
  const top = bands.length === 0 ? [] : bands[bands.length - 1].upper;
  const most = Math.max(0, ...top);
  return most === 0 ? 1 : most;
}

/** The x of the day at `index` of `count`, spread evenly across the drawing area. */
export function xAt(index: number, count: number, box: PlotBox = CHART): number {
  const innerWidth = box.width - box.left - box.right;
  return box.left + (count <= 1 ? 0 : (innerWidth / (count - 1)) * index);
}

/** The y of a count against the ceiling, zero at the baseline; the chart's height never narrows, so it reads CHART. */
export function yAt(value: number, ceiling: number): number {
  const innerHeight = CHART.height - CHART.top - CHART.bottom;
  return CHART.top + innerHeight - (value / ceiling) * innerHeight;
}

/** One band as the `d` of a closed SVG path: along its ceiling left to right, back along its floor. */
export function bandPath(band: ActivityBand, ceiling: number, box: PlotBox = CHART): string {
  const count = band.upper.length;
  if (count === 0) return '';
  const top = band.upper.map(
    (value, index) =>
      `${index === 0 ? 'M' : 'L'}${xAt(index, count, box).toFixed(1)} ${yAt(value, ceiling).toFixed(1)}`
  );
  const bottom = band.lower
    .map(
      (value, index) => `L${xAt(index, count, box).toFixed(1)} ${yAt(value, ceiling).toFixed(1)}`
    )
    .reverse();
  return `${[...top, ...bottom].join(' ')} Z`;
}

/** A band's ceiling alone, drawn in the card's ground over the fills: the two-pixel gap between bands. */
export function edgePath(band: ActivityBand, ceiling: number, box: PlotBox = CHART): string {
  const count = band.upper.length;
  return band.upper
    .map(
      (value, index) =>
        `${index === 0 ? 'M' : 'L'}${xAt(index, count, box).toFixed(1)} ${yAt(value, ceiling).toFixed(1)}`
    )
    .join(' ');
}

/**
 * Where a band's name is written on the chart: the middle of the day it is
 * thickest, anchored so it never leaves the drawing at either end; nowhere
 * when the band is never as thick as a line of text, in which case the legend
 * and the table carry it.
 */
export function labelSpot(
  band: ActivityBand,
  ceiling: number,
  box: PlotBox = CHART,
  minHeight = 16
): { x: number; y: number; anchor: 'start' | 'middle' | 'end' } | null {
  const count = band.upper.length;
  let best = -1;
  let thickest = 0;
  band.upper.forEach((value, index) => {
    const thickness = yAt(band.lower[index], ceiling) - yAt(value, ceiling);
    if (thickness > thickest) {
      thickest = thickness;
      best = index;
    }
  });
  if (best < 0 || thickest < minHeight) return null;
  return {
    x: xAt(best, count, box) + (best === 0 ? 6 : best === count - 1 ? -6 : 0),
    y: (yAt(band.upper[best], ceiling) + yAt(band.lower[best], ceiling)) / 2 + 4,
    anchor: best === 0 ? 'start' : best === count - 1 ? 'end' : 'middle',
  };
}
/**
 * Today's column, when the window ends on today (UTC): which point it is and
 * how many of its hours have passed, so the chart can say the last day is a
 * part-day and not a fall.
 */
export function partialDay(
  days: { day: string }[],
  now: Date
): { index: number; hours: number } | null {
  const index = days.length - 1;
  return index >= 0 && days[index].day === now.toISOString().slice(0, 10)
    ? { index, hours: now.getUTCHours() }
    : null;
}

/**
 * What a part-day says of itself: above the drawing at its right end, and in
 * the crosshair's box, never on the axis, where at a week it ran into the day
 * before it. The day is a UTC day and the hours are UTC hours, so the words
 * say UTC: at 8 am in Central time the day is already 13 hours in.
 */
export function todayNote(hours: number): string {
  return `today (UTC), ${hours} h in`;
}

/** The day nearest a point along the drawing, in the drawing's own units, never off either end. */
export function dayAt(x: number, count: number, box: PlotBox = CHART): number {
  if (count <= 1) return 0;
  const innerWidth = box.width - box.left - box.right;
  const index = Math.round(((x - box.left) / innerWidth) * (count - 1));
  return Math.min(count - 1, Math.max(0, index));
}
// #endregion stack

/** Which points carry an x label: about six across the width, the first and the last always. */
export function labelledIndexes(count: number): number[] {
  if (count <= 1) return count === 1 ? [0] : [];
  const every = Math.max(1, Math.round((count - 1) / 5));
  const indexes: number[] = [];
  for (let index = 0; index < count; index += every) indexes.push(index);
  if (indexes[indexes.length - 1] !== count - 1) indexes.push(count - 1);
  return indexes;
}

/** A day's x label: the weekday for a week, the date for a month, and the date for a day too, since a day window is two days. */
export function labelFor(at: string, window: ActivityWindow): string {
  // A UTC day, `2026-09-13`, read as that calendar day and not as the
  // instant before it in a western zone.
  const date = new Date(`${at}T12:00:00Z`);
  if (window === '7d') {
    return date.toLocaleDateString(undefined, { weekday: 'short', timeZone: 'UTC' });
  }
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });
}
// #endregion chart-geometry
