/**
 * Does:      Names the shape /api/pulse answers, reads an answer into it, and turns each figure into the words and the share the band draws.
 * Does not:  Fetch (the band does, with the page's own fetch), draw anything, or decide what a figure means (the server does).
 * Used by:   PulseBand.tsx, pulse.test.ts.
 */

/** One reading of the running site, as /api/pulse answers it. A null is unread, never zero. */
export type Pulse = {
  usual_p95_ms_24h: number | null;
  requests_24h: number | null;
  month_to_date: number | null;
  forecast_month: number | null;
  currency: string;
  cost_read_at_ms: number | null;
  uptime_seconds: number;
  version: string;
  commit: string;
  read_at_ms: number;
  /** People who have visited, all time: one per stable visitor hash the Domain's rule calls a person (ADR-116). Null until the first day is counted. */
  people_all_time: number | null;
  /** New people each day for the last thirty days, oldest first, today last; empty when the count is not read. */
  people_new_by_day: number[];
  /** The UTC day the count began (yyyy-mm-dd), or null on a server older than the count. */
  counted_from: string | null;
};

/** Where the band reads from, and how often it reads again while the page is open. */
export const PULSE_URL = '/api/pulse';
export const PULSE_EVERY_MS = 15_000;

/** The top of the latency scale, in milliseconds: a reading past it pins the marker and still says its number. */
export const LATENCY_CEILING_MS = 100;

const number = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);
const nullable = (value: unknown): value is number | null => value === null || number(value);

// #region pulse-from
/**
 * An answer read into the shape, or null when it is not that shape. A field this cannot read is a
 * band that says so, rather than a band drawing a figure it does not have.
 */
export function pulseFrom(answer: unknown): Pulse | null {
  if (typeof answer !== 'object' || answer === null) return null;
  const read = answer as Record<string, unknown>;
  if (
    !nullable(read.usual_p95_ms_24h) ||
    !nullable(read.requests_24h) ||
    !nullable(read.month_to_date) ||
    !nullable(read.forecast_month) ||
    !nullable(read.cost_read_at_ms) ||
    typeof read.currency !== 'string' ||
    !number(read.uptime_seconds) ||
    typeof read.version !== 'string' ||
    typeof read.commit !== 'string' ||
    !number(read.read_at_ms)
  ) {
    return null;
  }
  // The people count arrived with ADR-116; a server from before it answers without the three fields,
  // which reads as a count not yet begun rather than an answer of the wrong shape.
  const people = nullable(read.people_all_time) ? read.people_all_time : null;
  const days =
    Array.isArray(read.people_new_by_day) && read.people_new_by_day.every(number)
      ? (read.people_new_by_day as number[])
      : [];
  const countedFrom =
    typeof read.counted_from === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(read.counted_from)
      ? read.counted_from
      : null;
  return {
    usual_p95_ms_24h: read.usual_p95_ms_24h,
    requests_24h: read.requests_24h,
    month_to_date: read.month_to_date,
    forecast_month: read.forecast_month,
    currency: read.currency,
    cost_read_at_ms: read.cost_read_at_ms,
    uptime_seconds: read.uptime_seconds,
    version: read.version,
    commit: read.commit,
    read_at_ms: read.read_at_ms,
    people_all_time: people,
    people_new_by_day: days,
    counted_from: countedFrom,
  };
}
// #endregion pulse-from

// #region pulse-words
/** The word for an unread figure, in the one place every instrument takes it from. */
export const UNREAD = 'unread';

/**
 * The build a reading came from. Outside a build the server answers dev and local, which the
 * footer and the rail already write as "dev build"; the band says it the same way rather than
 * showing a reader two words that are not a version.
 */
export function buildWords(version: string, commit: string): string {
  return version === 'dev' ? 'dev build' : `${version} at ${commit}`;
}

/** How long since the roll, in the largest two units that say it: "7 h 33 m", "3 d 4 h", "41 m". */
export function uptimeWords(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return UNREAD;
  const minutes = Math.floor(seconds / 60);
  const days = Math.floor(minutes / (60 * 24));
  const hours = Math.floor((minutes - days * 60 * 24) / 60);
  if (days > 0) return `${days} d ${hours} h`;
  if (hours > 0) return `${hours} h ${minutes - hours * 60} m`;
  return `${minutes} m`;
}

/** Money as the band prints it: the symbol for the currencies this site bills in, whole cents. */
export function moneyWords(amount: number | null, currency: string, whole = false): string {
  if (amount === null || !Number.isFinite(amount)) return UNREAD;
  const symbol =
    currency === 'USD' ? '$' : currency === 'EUR' ? '€' : currency === 'GBP' ? '£' : '';
  const figure = whole
    ? Math.round(amount).toLocaleString('en-US')
    : amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return symbol === '' ? `${figure} ${currency}` : `${symbol}${figure}`;
}

/** Where the marker sits on a scale from zero to its ceiling: never past the end, never before the start. */
export function share(value: number | null, ceiling: number): number {
  if (value === null || !Number.isFinite(value) || !(ceiling > 0)) return 0;
  return Math.min(1, Math.max(0, value / ceiling));
}

/** The day a count began, as the band says it: "24 Oct". An unreadable day is the plain word for unread. */
export function dayWords(day: string | null): string {
  if (day === null) return UNREAD;
  const at = new Date(`${day}T00:00:00Z`);
  if (Number.isNaN(at.getTime())) return UNREAD;
  return at.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' });
}

/**
 * The thirty-day bar run as heights from 0 to 1 against the run's own tallest day, oldest first.
 * A run of zeros is thirty empty bars, not a division by zero.
 */
export function barHeights(days: number[]): number[] {
  const tallest = Math.max(0, ...days);
  return days.map((day) => (tallest > 0 ? Math.max(0, day) / tallest : 0));
}

/** The graduations of the latency scale: every five, with a number under every twenty-five. */
export function ticks(ceiling: number): { at: number; major: boolean }[] {
  const marks: { at: number; major: boolean }[] = [];
  for (let at = 0; at <= ceiling; at += 5) {
    marks.push({ at, major: at % 25 === 0 });
  }
  return marks;
}
// #endregion pulse-words
