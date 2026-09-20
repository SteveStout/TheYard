/**
 * The arithmetic behind the machine charts (ADR: What the machines are doing,
 * the addendum on drawing them). The same shape as the activity chart's lib:
 * plain functions over plain data, no React, so the drawing is a few paths and
 * the reasoning is testable on its own.
 */

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
};

export type TrafficSlot = {
  at: string;
  /** Requests a minute, or null where nothing was measured. */
  requests: number | null;
  p50_ms: number | null;
  p95_ms: number | null;
  server_errors: number | null;
  client_errors: number | null;
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

/** The sentence over the charts: what the slots add up to, from the slots themselves. */
export function trafficTotals(slots: TrafficSlot[], minutesPerSlot: number) {
  let requests = 0;
  let serverErrors = 0;
  let clientErrors = 0;
  let slowest: number | null = null;
  for (const slot of slots) {
    requests += (slot.requests ?? 0) * minutesPerSlot;
    serverErrors += (slot.server_errors ?? 0) * minutesPerSlot;
    clientErrors += (slot.client_errors ?? 0) * minutesPerSlot;
    if (slot.p95_ms !== null && (slowest === null || slot.p95_ms > slowest)) slowest = slot.p95_ms;
  }
  return {
    requests: Math.round(requests),
    server_errors: Math.round(serverErrors),
    client_errors: Math.round(clientErrors),
    slowest_p95_ms: slowest,
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
