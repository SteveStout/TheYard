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
