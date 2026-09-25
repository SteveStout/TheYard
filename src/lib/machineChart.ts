/**
 * The arithmetic behind the machine charts (ADR: What the machines are doing,
 * the addendum on drawing them). The same shape as the activity chart's lib:
 * plain functions over plain data, no React, so the drawing is a few paths and
 * the reasoning is testable on its own.
 */

import { plotFrame } from './plotFrame';

/** One reading. A null value is a gap rather than a zero: the first processor share has nothing to compare against. */
export type ChartPoint = { at: string; value: number | null };

export type ChartSeries = { key: string; name: string; points: ChartPoint[] };

export const MACHINE_CHART = {
  width: 720,
  height: 170,
  left: 44,
  right: 12,
  top: 12,
  bottom: 24,
} as const;

/**
 * The top of the axis: the highest reading rounded up to something a person
 * reads, never zero. A percentage chart passes 100 as the floor so a quiet
 * hour is drawn along the bottom of a full axis rather than filling it.
 */
export function ceilingFor(series: ChartSeries[], atLeast = 1): number {
  let top = atLeast;
  for (const line of series) {
    for (const point of line.points) {
      if (point.value !== null && point.value > top) top = point.value;
    }
  }
  if (top <= 1) return 1;
  const magnitude = Math.pow(10, Math.floor(Math.log10(top)));
  for (const step of [1, 2, 2.5, 5, 10]) {
    const candidate = step * magnitude;
    if (top <= candidate) return Number(candidate.toFixed(4));
  }
  return 10 * magnitude;
}

/**
 * The line, with a break wherever a reading is missing: a gap drawn as a
 * straight line across it would be a number nobody measured.
 */
export function pathFor(points: ChartPoint[], ceiling: number): string {
  const innerWidth = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  const innerHeight = MACHINE_CHART.height - MACHINE_CHART.top - MACHINE_CHART.bottom;
  const step = points.length <= 1 ? 0 : innerWidth / (points.length - 1);
  let path = '';
  let penDown = false;
  points.forEach((point, index) => {
    if (point.value === null) {
      penDown = false;
      return;
    }
    const x = MACHINE_CHART.left + index * step;
    const y =
      MACHINE_CHART.top + innerHeight - (Math.min(point.value, ceiling) / ceiling) * innerHeight;
    path += `${path === '' || !penDown ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)} `;
    penDown = true;
  });
  return path.trim();
}

// #region readout
/**
 * Which reading a pointer is over (ADR: The glass look, the readout on a
 * chart): the nearest slot to an x in the drawing's own units, clamped to the
 * plot, so a finger at the edge of the card reads the first or the last slot
 * and never nothing.
 */
export function nearestIndex(x: number, count: number): number | null {
  if (count <= 0 || Number.isNaN(x)) return null;
  if (count === 1) return 0;
  const innerWidth = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  const step = innerWidth / (count - 1);
  return Math.min(count - 1, Math.max(0, Math.round((x - MACHINE_CHART.left) / step)));
}

/** Where a slot is drawn, across: the same arithmetic the line uses. */
export function xOf(index: number, count: number): number {
  const innerWidth = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  return MACHINE_CHART.left + (count <= 1 ? 0 : (index * innerWidth) / (count - 1));
}

/** One line of the readout: a reading in the axis's unit, or the words for a slot nobody measured. */
export function readoutLine(name: string, value: number | null, unit?: string): string {
  if (value === null) return `${name}: not measured`;
  const shown = value.toLocaleString('en-US');
  return `${name}: ${shown}${unit === undefined ? '' : unit === '%' ? '%' : ` ${unit}`}`;
}

// #endregion readout

// #region mark-vii
/**
 * The Mark VII frame for a machine chart of `count` slots (the tweaks pass, B2),
 * in place of the grid the charts drew until 1.0.3.23: the labelled slots major
 * along the bottom, eight even steps minor, and the side and brackets every
 * framed chart shares (src/lib/plotFrame.ts).
 */
export function machineFrame(count: number) {
  const inner = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  const minor = Array.from({ length: 9 }, (_, step) => ({
    key: `m${step}`,
    x: MACHINE_CHART.left + (inner * step) / 8,
    major: false,
  }));
  const major = ticks(count).map((index) => ({
    key: `x${index}`,
    x: xOf(index, count),
    major: true,
  }));
  return plotFrame(MACHINE_CHART, [...minor, ...major]);
}

/** A series' highest reading and where it is drawn, for the callout; null when nothing rose above zero. */
export function peakOf(
  points: ChartPoint[],
  ceiling: number
): { index: number; value: number; x: number; y: number } | null {
  let index = -1;
  let value = 0;
  points.forEach((point, at) => {
    if (point.value !== null && point.value > value) {
      value = point.value;
      index = at;
    }
  });
  if (index < 0 || ceiling <= 0) return null;
  const innerHeight = MACHINE_CHART.height - MACHINE_CHART.top - MACHINE_CHART.bottom;
  const y = MACHINE_CHART.top + innerHeight - (Math.min(value, ceiling) / ceiling) * innerHeight;
  return {
    index,
    value,
    x: Math.round(xOf(index, points.length) * 10) / 10,
    y: Math.round(y * 10) / 10,
  };
}

/** The callout's leader: out from the peak, down from the top of the plot, then a shelf the label sits on. */
const CALLOUT = { reach: 14, drop: 10, shelf: 8 } as const;

/**
 * The peak callout (the tweaks pass, B2), placed: the dot on the peak, a leader
 * up to an elbow under the top of the plot, and the label running away from the
 * nearer edge. Null when the series is not there or never rose above zero.
 */
export function calloutFor(
  series: ChartSeries[],
  callout: { key: string; name: string },
  ceiling: number,
  window: MachineWindow
): {
  label: string;
  dot: { x: number; y: number };
  leader: string;
  text: { x: number; y: number; anchor: 'start' | 'end' };
} | null {
  const line = series.find((one) => one.key === callout.key);
  const peak = line === undefined ? null : peakOf(line.points, ceiling);
  if (line === undefined || peak === null) return null;
  const toRight = peak.x < MACHINE_CHART.width / 2;
  const away = toRight ? 1 : -1;
  const elbow = { x: peak.x + away * CALLOUT.reach, y: MACHINE_CHART.top + CALLOUT.drop };
  return {
    label: `${callout.name} · peak ${peak.value.toLocaleString()} at ${axisLabel(line.points[peak.index].at, window)}`,
    dot: { x: peak.x, y: peak.y },
    leader: `${peak.x},${peak.y} ${elbow.x},${elbow.y} ${elbow.x + away * CALLOUT.shelf},${elbow.y}`,
    text: {
      x: elbow.x + away * (CALLOUT.shelf + 3),
      y: elbow.y + 3,
      anchor: toRight ? 'start' : 'end',
    },
  };
}

// #endregion mark-vii

/** Up to three evenly spaced indexes to label, first and last always. */
export function ticks(count: number): number[] {
  if (count <= 1) return count === 1 ? [0] : [];
  if (count === 2) return [0, 1];
  return [0, Math.floor((count - 1) / 2), count - 1];
}

/** A reading's time as a clock reads it, which is the only axis a one-hour window needs. */
export function clockLabel(at: string): string {
  const when = new Date(at);
  return Number.isNaN(when.getTime())
    ? ''
    : `${String(when.getHours()).padStart(2, '0')}:${String(when.getMinutes()).padStart(2, '0')}`;
}

// #region kept-windows
/**
 * The windows the machine charts offer (ADR: What the machines are doing, the
 * addendum on the windows). The hour is the process's own memory; the other
 * three are read from the store in buckets, and the arithmetic that turns
 * buckets into lines lives here, where it can be tested without a page.
 */
export const MACHINE_WINDOWS = ['1h', '24h', '7d', '30d'] as const;
export type MachineWindow = (typeof MACHINE_WINDOWS)[number];

export function windowName(window: MachineWindow): string {
  switch (window) {
    case '1h':
      return 'Last hour';
    case '24h':
      return 'Last 24 hours';
    case '7d':
      return 'Last 7 days';
    default:
      return 'Last 30 days';
  }
}

/** One bucket of a kept window, as the endpoint answers it. A figure nobody read is null. */
export type KeptBucket = {
  at: string;
  minutes: number;
  memory_limit_mb: number;
  working_set_mb: number;
  working_set_max_mb: number;
  managed_mb: number;
  cpu_percent: number | null;
  cpu_max_percent: number | null;
  sql_cpu_percent: number | null;
  sql_memory_percent: number | null;
  sql_data_io_percent: number | null;
  request_units: number;
  operations: number;
  requests: number;
  p50_ms: number | null;
  p95_ms: number | null;
  server_errors: number;
  client_errors: number;
};

const WINDOW_MINUTES: Record<Exclude<MachineWindow, '1h'>, number> = {
  '24h': 24 * 60,
  '7d': 7 * 24 * 60,
  '30d': 30 * 24 * 60,
};

/**
 * The whole window as a timeline, one slot per bucket from the window's start
 * to now, with null wherever the store holds nothing: an hour the site was
 * down, or the weeks before anything was kept. The charts draw a slot with no
 * bucket as a break in the line. Drawing only the buckets that exist would
 * close every gap up and make a site that was down for a day look like one
 * that never was.
 */
export function timeline(
  buckets: KeptBucket[],
  window: Exclude<MachineWindow, '1h'>,
  bucketMinutes: number,
  now: Date
): { at: string; bucket: KeptBucket | null }[] {
  if (bucketMinutes <= 0) return [];
  const stepMs = bucketMinutes * 60_000;
  const end = Math.floor(now.getTime() / stepMs) * stepMs;
  const count = Math.round(WINDOW_MINUTES[window] / bucketMinutes);
  const byStart = new Map<number, KeptBucket>();
  for (const bucket of buckets) {
    const at = new Date(bucket.at).getTime();
    if (!Number.isNaN(at)) byStart.set(Math.floor(at / stepMs) * stepMs, bucket);
  }
  const slots: { at: string; bucket: KeptBucket | null }[] = [];
  for (let index = count - 1; index >= 0; index -= 1) {
    const start = end - index * stepMs;
    slots.push({ at: new Date(start).toISOString(), bucket: byStart.get(start) ?? null });
  }
  return slots;
}

/** Memory as a share of the limit it was read against, to one decimal, or null when either is missing. */
export function shareOf(valueMb: number | null | undefined, limitMb: number | null | undefined) {
  if (valueMb === null || valueMb === undefined || !limitMb || limitMb <= 0) return null;
  return Math.round((valueMb / limitMb) * 1000) / 10;
}

/** What a bucket was charged, as request units a minute over the minutes it actually holds. */
/**
 * The busiest minute in the ring as request units a second (the self-review of
 * 25 September): the one figure a free tier's allowance, a rate, can be held
 * against. The ring's total was held against it until then, a total against a rate.
 */
export function busiestRate(minutes: { request_units: number }[]): number {
  const busiest = minutes.reduce((most, minute) => Math.max(most, minute.request_units), 0);
  return Math.round((busiest / 60) * 10) / 10;
}

export function requestUnitsAMinute(bucket: KeptBucket | null): number | null {
  if (bucket === null || bucket.minutes <= 0) return null;
  return Math.round((bucket.request_units / bucket.minutes) * 100) / 100;
}

/**
 * An axis label sized to the window: a clock for an hour or a day, the day of
 * the month for a week or a month, where a clock time would say nothing.
 */
export function axisLabel(at: string, window: MachineWindow): string {
  if (window === '1h' || window === '24h') return clockLabel(at);
  const when = new Date(at);
  if (Number.isNaN(when.getTime())) return '';
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return `${when.getDate()} ${months[when.getMonth()]}`;
}

/** How much of a window the store actually holds, for the sentence under the buttons. */
// #region from-first-reading
/**
 * Where a kept window's charts start. The window is asked for whole, and what
 * it holds is counted against the whole of it, but the drawing starts at the
 * first reading the store has: on the day the minutes began to be kept, a
 * month drawn whole was one sliver at the right-hand edge of an empty frame,
 * and read as a chart that was broken (ADR: The Admin tab, as a product, the
 * addendum on where a window starts). Only the emptiness before the record
 * began is left off. A gap after the first reading is the site not reporting,
 * and stays a gap. A record younger than a dozen slots keeps a dozen, so the
 * first hour is a short line at the right of a small frame and not a dot.
 */
export const LEAST_SLOTS = 12;

export function fromFirstReading<T extends { bucket: KeptBucket | null }>(slots: T[]): T[] {
  const first = slots.findIndex((slot) => slot.bucket !== null);
  if (first < 0) return slots;
  return slots.slice(Math.min(first, Math.max(0, slots.length - LEAST_SLOTS)));
}
// #endregion from-first-reading

export function coverage(slots: { bucket: KeptBucket | null }[]): { held: number; of: number } {
  return { held: slots.filter((slot) => slot.bucket !== null).length, of: slots.length };
}
// #endregion kept-windows

// #region traffic
/**
 * Traffic, in one shape whatever the window (ADR: The Admin tab, as a
 * product). The hour comes from the request ring a minute at a time; a kept
 * window comes from buckets of kept minutes. Both become the same slots, so
 * the four traffic charts are drawn by one piece of code and cannot disagree
 * about what a gap or a zero means: a slot nobody measured is null, and a
 * measured slot in which nobody asked for anything is zero requests with no
 * median, because there is no median of nothing.
 */
export type TrafficMinute = {
  at: string;
  requests: number;
  p50_ms: number;
  p95_ms: number;
  server_errors: number;
  client_errors: number;
  /** Every request's time in the minute, sorted, for the hour's own percentiles (statTiles.ts, hourTiming). */
  durations_ms?: number[];
};

export type TrafficSlot = {
  at: string;
  /** Requests a minute, or null where nothing was measured. */
  requests: number | null;
  p50_ms: number | null;
  p95_ms: number | null;
  server_errors: number | null;
  client_errors: number | null;
  /** The minute's request times, sorted; absent on a kept bucket, which holds only its percentiles. */
  durations_ms?: number[];
};

/**
 * The hour the ring holds: every minute from the oldest request it still has,
 * or an hour ago if that is later, to now. Minutes inside that span with no
 * request are true zeros, because the process was up and nobody asked.
 */
export function hourOfTraffic(minutes: TrafficMinute[], asOf: Date): TrafficSlot[] {
  const step = 60_000;
  const end = Math.floor(asOf.getTime() / step) * step;
  const held = new Map<number, TrafficMinute>();
  let oldest = end;
  for (const minute of minutes) {
    const at = Math.floor(new Date(minute.at).getTime() / step) * step;
    if (Number.isNaN(at)) continue;
    held.set(at, minute);
    if (at < oldest) oldest = at;
  }
  const start = Math.max(oldest, end - 59 * step);
  const slots: TrafficSlot[] = [];
  for (let at = start; at <= end; at += step) {
    const minute = held.get(at);
    slots.push({
      at: new Date(at).toISOString(),
      requests: minute?.requests ?? 0,
      p50_ms: minute?.p50_ms ?? null,
      p95_ms: minute?.p95_ms ?? null,
      server_errors: minute?.server_errors ?? 0,
      client_errors: minute?.client_errors ?? 0,
      durations_ms: minute?.durations_ms ?? [],
    });
  }
  return slots;
}

/** A kept window's slots as traffic: counts become a rate a minute over the minutes the bucket holds. */
export function keptTraffic(slots: { at: string; bucket: KeptBucket | null }[]): TrafficSlot[] {
  const rate = (count: number, minutes: number) =>
    minutes <= 0 ? null : Math.round((count / minutes) * 100) / 100;
  return slots.map(({ at, bucket }) =>
    bucket === null
      ? { at, requests: null, p50_ms: null, p95_ms: null, server_errors: null, client_errors: null }
      : {
          at,
          requests: rate(bucket.requests, bucket.minutes),
          p50_ms: bucket.p50_ms,
          p95_ms: bucket.p95_ms,
          server_errors: rate(bucket.server_errors, bucket.minutes),
          client_errors: rate(bucket.client_errors, bucket.minutes),
        }
  );
}

/**
 * The lines under the tiles over a kept window (ADR: The Admin tab, as a
 * product, the addendum on one window for every chart): the same four
 * readings the hour's lines are drawn from, one value a bucket, and null
 * where the store holds no bucket, so a day the site was down is a gap under
 * the tile exactly as it is on the chart.
 */
export function keptSparks(slots: { at: string; bucket: KeptBucket | null }[]): {
  speed: (number | null)[];
  memory: (number | null)[];
  charged: (number | null)[];
  errors: (number | null)[];
} {
  return {
    // The line under the speed tile is the typical answer, the same reading as the number over it.
    speed: slots.map(({ bucket }) => bucket?.p50_ms ?? null),
    memory: slots.map(({ bucket }) => bucket?.working_set_mb ?? null),
    charged: slots.map(({ bucket }) => bucket?.request_units ?? null),
    errors: slots.map(({ bucket }) => bucket?.server_errors ?? null),
  };
}

/** The sentence over the charts: what the slots add up to, from the slots themselves. */
export function trafficTotals(slots: TrafficSlot[], minutesPerSlot: number) {
  let requests = 0;
  let serverErrors = 0;
  let clientErrors = 0;
  let slowest: number | null = null;
  let slowestAt: string | null = null;
  for (const slot of slots) {
    requests += (slot.requests ?? 0) * minutesPerSlot;
    serverErrors += (slot.server_errors ?? 0) * minutesPerSlot;
    clientErrors += (slot.client_errors ?? 0) * minutesPerSlot;
    if (slot.p95_ms !== null && (slowest === null || slot.p95_ms > slowest)) {
      slowest = slot.p95_ms;
      slowestAt = slot.at;
    }
  }
  return {
    requests: Math.round(requests),
    server_errors: Math.round(serverErrors),
    client_errors: Math.round(clientErrors),
    slowest_p95_ms: slowest,
    // Where to look: a tile that says "slow" and not "when" sends somebody through an hour of rows.
    slowest_at: slowestAt,
  };
}

/**
 * The proof's paired medians as bars (ADR: Same performance, proven): one
 * pair a path, each bar a share of the longest median on the card, so the
 * eye reads what the table says, that most pairs are the same length and the
 * ones that are not differ by a round trip.
 */
export type PairedBar = { label: string; bars: { store: string; ms: number; share: number }[] };

export function pairedBars(
  rows: { label: string; cells: { store: string; samples: number; p50_ms: number }[] }[]
): PairedBar[] {
  const measured = rows
    .map((row) => ({ label: row.label, cells: row.cells.filter((cell) => cell.samples > 0) }))
    .filter((row) => row.cells.length > 0);
  const longest = measured.reduce(
    (most, row) => Math.max(most, ...row.cells.map((cell) => cell.p50_ms)),
    0
  );
  return measured.map((row) => ({
    label: row.label,
    bars: row.cells.map((cell) => ({
      store: cell.store,
      ms: cell.p50_ms,
      // A bar for a zero is still drawn, one per cent wide, so a path both
      // stores answer in no time reads as two equal bars and not as nothing.
      share: longest <= 0 ? 1 : Math.max(1, Math.round((cell.p50_ms / longest) * 100)),
    })),
  }));
}
// #endregion traffic
