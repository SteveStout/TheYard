// The activity card's arithmetic, kept out of the component so it can be
// tested without a browser: the wire shapes, the line a series draws, and the
// order the visitor table sorts into (ADR: Site activity, and the line an
// address does not cross).

import type { PlotBox } from './plotFrame';

export type ActivityWindow = '24h' | '7d' | '30d';
export const ACTIVITY_WINDOWS: readonly ActivityWindow[] = ['24h', '7d', '30d'];

export type ActivityPoint = { at: string; requests: number; bots: number };
export type ActivitySeries = { store: string; name: string; points: ActivityPoint[] };
export type ActivityStoreState = {
  store: string;
  name: string;
  available: boolean;
  reason: string;
};
export type ActivityPath = { path: string; requests: number };
/**
 * Who a visitor-day was (1.0.3.11): people, scanners and crawlers, and the
 * site's own reads (its tools under the self mark, and App Service asking
 * after the container from the loopback address). The three add up to the
 * day's visitor-days.
 */
export type ActivityKinds = { people: number; scanners: number; self: number };
export type ActivityDay = ActivityKinds & {
  day: string;
  visitors: number;
  humans: number;
  bots: number;
  by_store: ({ store: string; visitors: number } & ActivityKinds)[];
};
/** The recruiter's path (1.0.3.13): the four steps the site exists for, in the order they are walked. */
export type ActivityStep = 'site' | 'inventory' | 'author' | 'resume';
export const STEP_NAMES: Readonly<Record<ActivityStep, string>> = {
  site: 'Opened the site',
  inventory: 'The inventory',
  author: 'About Steven',
  resume: 'The resume',
};

/**
 * Each step's bar as a share of the widest, which is the first step when the
 * steps are walked in order; a step anybody reached is never drawn thinner
 * than a sliver, so one resume opened among a thousand visits still shows.
 */
export function pathShares(path: { visitor_days: number }[]): number[] {
  const most = Math.max(0, ...path.map((step) => step.visitor_days));
  return path.map((step) =>
    most === 0 || step.visitor_days === 0 ? 0 : Math.max(0.02, step.visitor_days / most)
  );
}

/** One kind of traffic over the window: visitor-days summed over the days, requests, what it asked for, per store. */
export type ActivityWhoEntry = {
  visitor_days: number;
  requests: number;
  top_paths: ActivityPath[];
  path: { step: ActivityStep; visitor_days: number }[];
  sources: { host: string; visitor_days: number }[];
  by_store: { store: string; visitor_days: number; requests: number }[];
};
/** The card's toggle: visitors only (the people) or all traffic (the three kinds together). */
export type ActivityWho = 'people' | 'all';
export const ACTIVITY_WHO: readonly ActivityWho[] = ['people', 'all'];
export type ActivityReport = {
  window: ActivityWindow;
  /** Whether this site serves the per-visitor rows at all (off by default since 13 September). */
  visitor_rows: boolean;
  bucket: string;
  since: string;
  until: string;
  totals: { requests: number; bots: number; humans: number };
  by_store: { store: string; requests: number; bots: number; humans: number }[];
  series: ActivitySeries[];
  days: ActivityDay[];
  who: {
    people: ActivityWhoEntry;
    scanners: ActivityWhoEntry;
    self: ActivityWhoEntry;
    all: ActivityWhoEntry;
  };
  top_paths: ActivityPath[];
  stores: ActivityStoreState[];
  /** The key of the one store every batch is written to (14 September). */
  kept_by: string;
  /** The keeper's own sentence for how long rows are kept, when it is up ("kept in Azure Cosmos DB with no expiry"). */
  retention: string | null;
  collector: {
    offered: number;
    written: number;
    failed_batches: number;
    /** Hits a full channel dropped, oldest first, since the process started (25 September). */
    dropped: number;
    last_write: string | null;
    interval_seconds: number;
  };
  /** What keeping activity has cost the keeper since the process started; null on a store with no unit for it. */
  cost: { request_units: number; operations: number; failures: number } | null;
};

export type ActivityVisitor = {
  visitor: string;
  network: string;
  store: string;
  day: string;
  first_seen: string;
  last_seen: string;
  requests: number;
  bots: number;
  top_paths: ActivityPath[];
};
export type ActivityVisitors = {
  window: ActivityWindow;
  since: string;
  until: string;
  count: number;
  visitors: ActivityVisitor[];
};

// #region days
/** A day's visitor-days under the toggle: the people only, or the three kinds together. */
export function countFor(kinds: ActivityKinds, who: ActivityWho): number {
  return who === 'people' ? kinds.people : kinds.people + kinds.scanners + kinds.self;
}

/**
 * The graph's series: unique visitors per UTC day (Steve's ask, 13
 * September, "per all unique ips per day"), one line for everybody and one
 * per store, on the point shape the line geometry below already draws.
 * Under Visitors only every line counts people; under All traffic, all
 * three kinds (1.0.3.11).
 */
export function dayLines(
  days: ActivityDay[],
  stores: string[],
  who: ActivityWho = 'all'
): ActivitySeries[] {
  const all: ActivitySeries = {
    store: 'all',
    name: who === 'people' ? 'People' : 'All traffic',
    points: days.map((day) => ({ at: day.day, requests: countFor(day, who), bots: day.scanners })),
  };
  const perStore = stores.map((store) => ({
    store,
    name: store,
    points: days.map((day) => {
      const entry = day.by_store.find((candidate) => candidate.store === store);
      return { at: day.day, requests: entry ? countFor(entry, who) : 0, bots: 0 };
    }),
  }));
  return [all, ...perStore];
}

/** The visitor rows grouped by UTC day, newest day first, with the day's own totals for its heading. */
export function groupByDay(
  rows: ActivityVisitor[]
): { day: string; visitors: number; requests: number; rows: ActivityVisitor[] }[] {
  const byDay = new Map<string, ActivityVisitor[]>();
  for (const row of rows) {
    const list = byDay.get(row.day) ?? [];
    list.push(row);
    byDay.set(row.day, list);
  }
  return [...byDay.entries()]
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([day, dayRows]) => ({
      day,
      visitors: new Set(dayRows.map((row) => row.visitor)).size,
      requests: dayRows.reduce((sum, row) => sum + row.requests, 0),
      rows: dayRows,
    }));
}
// #endregion days

// #region page-names
/**
 * A path as the page a person asked for (1.0.3.16): the listing, the facets,
 * About Steven, and not the route, so the list reads as what people looked at.
 * The most particular pattern first; a path nothing names is itself.
 */
const PAGE_NAMES: readonly (readonly [RegExp, string])[] = [
  [/^\/(index\.html)?$/, 'Opened the site'],
  [/^\/api\/vehicles$/, 'Inventory listing'],
  [/^\/api\/vehicles\/[^/]+\/bids$/, "One vehicle's bids"],
  [/^\/api\/vehicles\/[^/]+$/, 'One vehicle'],
  [/^\/api\/facets$/, 'Facets and filters'],
  [/^\/api\/bids$/, 'Bids'],
  [/^\/api\/auth\/me$/, 'Who am I (sign-in state)'],
  [/^\/api\/stores$/, 'The store bar'],
  [/^\/api\/version$/, 'The version line'],
  [/^\/api\/tests\/summary$/, 'The test counts'],
  [/^\/api\/docs\/author$/, 'About Steven'],
  [/^(\/api\/docs\/resume|\/docs\/resume\.pdf|\/resume\.pdf)$/, 'The resume'],
  [/^\/api\/docs\/(.+)$/, 'Document: $1'],
  [/^\/api\/admin\/(.+)$/, 'Admin: $1'],
];

export function pageName(path: string): string {
  for (const [pattern, name] of PAGE_NAMES) {
    const match = pattern.exec(path);
    if (match) return name.replace('$1', match[1] ?? '');
  }
  return path;
}

/** Paths by page name, two paths that are one page added together, most asked for first. */
export function namedPaths(paths: ActivityPath[]): { name: string; requests: number }[] {
  const merged = new Map<string, number>();
  for (const entry of paths) {
    const name = pageName(entry.path);
    merged.set(name, (merged.get(name) ?? 0) + entry.requests);
  }
  return [...merged.entries()]
    .map(([name, requests]) => ({ name, requests }))
    .sort((a, b) => b.requests - a.requests || a.name.localeCompare(b.name));
}

/**
 * Where they came from (1.0.3.17): the host of the page that linked here, put in
 * one of five groups, in a fixed order. A host nothing names is another site;
 * "(none)" is a page load with no referring page, typed, bookmarked, or opened
 * from something that says nothing, a PDF among them.
 */
export type SourceGroup = 'linkedin' | 'github' | 'search' | 'other' | 'none';
export const SOURCE_GROUPS: readonly SourceGroup[] = [
  'linkedin',
  'github',
  'search',
  'other',
  'none',
];
export const SOURCE_NAMES: Readonly<Record<SourceGroup, string>> = {
  linkedin: 'LinkedIn',
  github: 'GitHub',
  search: 'Search',
  other: 'Another site',
  none: 'Typed or unknown',
};

export function sourceGroup(host: string): SourceGroup {
  if (host === '(none)') return 'none';
  if (/(^|\.)linkedin\.com$|^lnkd\.in$/.test(host)) return 'linkedin';
  if (/(^|\.)github\.(com|io)$/.test(host)) return 'github';
  if (
    /(^|\.)(google\.[a-z.]+|bing\.com|duckduckgo\.com|yahoo\.com|ecosia\.org|baidu\.com|yandex\.[a-z]+|startpage\.com)$/.test(
      host
    ) ||
    host === 'search.brave.com'
  )
    return 'search';
  return 'other';
}

/** The hosts added up by group, every group present and in the fixed order, so the tile's rows never move. */
export function groupSources(
  sources: { host: string; visitor_days: number }[]
): { group: SourceGroup; visitor_days: number }[] {
  const totals = new Map<SourceGroup, number>(SOURCE_GROUPS.map((group) => [group, 0]));
  for (const entry of sources) {
    const group = sourceGroup(entry.host);
    totals.set(group, (totals.get(group) ?? 0) + entry.visitor_days);
  }
  return SOURCE_GROUPS.map((group) => ({ group, visitor_days: totals.get(group) ?? 0 }));
}
// #endregion page-names

// #region chart-geometry
/**
 * The drawing area the lines are laid into, in SVG units: the desk's, which the
 * card scales up to its width, and narrowed to the width a phone gives it
 * (fitBox in src/lib/plotFrame.ts, 1.0.3.30) so its words keep their size.
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
 * The three kinds of traffic stacked (1.0.3.12): people at the bottom, then
 * scanners and crawlers, then the site's own reads, in that order always, so
 * a colour means the same kind on every window. Under Visitors only the stack
 * is the people alone. A band is the day-by-day floor and ceiling it fills
 * between, in visitor-days.
 */
export type ActivityKind = 'people' | 'scanners' | 'self';
export const ACTIVITY_KINDS: readonly ActivityKind[] = ['people', 'scanners', 'self'];
export const KIND_NAMES: Readonly<Record<ActivityKind, string>> = {
  people: 'People',
  scanners: 'Scanners and crawlers',
  self: "The site's own reads",
};
export type ActivityBand = { kind: ActivityKind; lower: number[]; upper: number[] };

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

export function xAt(index: number, count: number, box: PlotBox = CHART): number {
  const innerWidth = box.width - box.left - box.right;
  return box.left + (count <= 1 ? 0 : (innerWidth / (count - 1)) * index);
}

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
 * What a part-day says of itself (1.0.3.15): above the drawing at its right
 * end, and in the crosshair's box, never on the axis, where at a week it ran
 * into the day before it.
 */
export function todayNote(hours: number): string {
  return `today, ${hours} h in`;
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

// #region visitor-order
export type VisitorSortKey = 'last_seen' | 'first_seen' | 'requests' | 'network' | 'store';

/** The table's order: newest first by default, and any column either way on a click. */
export function sortVisitors(
  rows: ActivityVisitor[],
  key: VisitorSortKey,
  descending: boolean
): ActivityVisitor[] {
  const sorted = [...rows].sort((a, b) => {
    const left = a[key];
    const right = b[key];
    if (typeof left === 'number' && typeof right === 'number') return left - right;
    return String(left).localeCompare(String(right));
  });
  return descending ? sorted.reverse() : sorted;
}
// #endregion visitor-order

// #region collector-words
/** The collector's one line: fine, or what went wrong first (failed batches outrank drops). */
export function collectorSummary(collector: ActivityReport['collector']): string {
  const plural = (count: number, one: string, many: string) =>
    `${count.toLocaleString()} ${count === 1 ? one : many}`;
  if (collector.failed_batches > 0)
    return `Collector: ${plural(collector.failed_batches, 'batch', 'batches')} failed`;
  if (collector.dropped > 0)
    return `Collector: ${plural(collector.dropped, 'hit', 'hits')} dropped`;
  return 'Collector fine';
}

/**
 * What keeping activity has cost, in a sentence. The count is this container's
 * own since it started: the other container writes to the same keeper and
 * counts its own, so the sentence says whose it is.
 */
export function costSentence(cost: NonNullable<ActivityReport['cost']>): string {
  const failed = cost.failures > 0 ? `, ${cost.failures.toLocaleString()} of them failed` : '';
  return `Keeping it has cost this container ${cost.request_units.toLocaleString()} request units over ${cost.operations.toLocaleString()} operations since it started${failed}.`;
}
// #endregion collector-words
