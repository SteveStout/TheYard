/**
 * The windows the machine charts offer, an hour, a day, a week and a month,
 * and the arithmetic that turns a kept window's buckets into slots a chart
 * draws: the timeline with its gaps, where the drawing starts, how much the
 * store holds, and the label along the bottom. Its own file because a kept
 * window is read from the store, and the hour and the traffic are not.
 */

import { clockLabel } from './machineChartPlot';

// #region kept-windows
/**
 * The windows the machine charts offer (ADR: What the machines are doing, the
 * addendum on the windows). The hour is the process's own memory; the other
 * three are read from the store in buckets, and the arithmetic that turns
 * buckets into lines lives here, where it can be tested without a page.
 */
export const MACHINE_WINDOWS = ['1h', '24h', '7d', '30d'] as const;

/** One of the four windows, by its short name. */
export type MachineWindow = (typeof MACHINE_WINDOWS)[number];

/** A window's name as the buttons and sentences write it. */
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

/** How many minutes each kept window spans, to count the slots its timeline holds. */
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

/**
 * The busiest minute in the ring as request units a second: the one figure a
 * free tier's allowance, which is a rate, can be held against. A total over
 * the ring is not a rate and cannot be.
 */
export function busiestRate(minutes: { request_units: number }[]): number {
  const busiest = minutes.reduce((most, minute) => Math.max(most, minute.request_units), 0);
  return Math.round((busiest / 60) * 10) / 10;
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

// #region from-first-reading
/**
 * Where a kept window's charts start. The window is asked for whole, and what
 * it holds is counted against the whole of it, but the drawing starts at the
 * first reading the store has: a month drawn whole over a record a day old is
 * one sliver at the right-hand edge of an empty frame, and reads as a chart
 * that is broken (ADR: The Admin tab, as a product, the addendum on where a
 * window starts). Only the emptiness before the record began is left off. A
 * gap after the first reading is the site not reporting, and stays a gap. A
 * record younger than a dozen slots keeps a dozen, so the first hour is a
 * short line at the right of a small frame and not a dot.
 */
export const LEAST_SLOTS = 12;

/** The slots from the first reading on, keeping at least LEAST_SLOTS of them, as described above. */
export function fromFirstReading<T extends { bucket: KeptBucket | null }>(slots: T[]): T[] {
  const first = slots.findIndex((slot) => slot.bucket !== null);
  if (first < 0) return slots;
  return slots.slice(Math.min(first, Math.max(0, slots.length - LEAST_SLOTS)));
}
// #endregion from-first-reading

/** How much of a window the store actually holds, for the sentence under the buttons. */
export function coverage(slots: { bucket: KeptBucket | null }[]): { held: number; of: number } {
  return { held: slots.filter((slot) => slot.bucket !== null).length, of: slots.length };
}
// #endregion kept-windows
