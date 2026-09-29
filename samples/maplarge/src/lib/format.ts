/**
 * Numbers and instants as a person reads them. The server sends bytes and
 * milliseconds; how they look is the page's decision (ADR-004), and it is made
 * here once so the table, the totals line and the upload notice agree.
 */

const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/**
 * A byte count as "1.2 MB": one decimal above bytes, none for bytes, thousands
 * with a separator. 1024 to a step, because that is what the file system means.
 */
export function bytes(count: number): string {
  if (!Number.isFinite(count) || count < 0) {
    return '';
  }
  let value = count;
  let unit = 0;
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const digits = unit === 0 ? 0 : 1;
  return `${value.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })} ${UNITS[unit]}`;
}

/**
 * Milliseconds since the epoch as a short local date and time, "2026-09-29 13:05",
 * or "today 13:05" when it is today.
 * @param now injected so a test can pin the day
 */
export function when(ms: number, now: Date = new Date()): string {
  if (!Number.isFinite(ms) || ms <= 0) {
    return '';
  }
  const date = new Date(ms);
  const pad = (n: number) => String(n).padStart(2, '0');
  const day = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  const today = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
  return day === today ? `today ${time}` : `${day} ${time}`;
}

/** A count with its noun: "1 folder", "12 files". */
export function plural(count: number, singular: string, many: string = `${singular}s`): string {
  return `${count.toLocaleString()} ${count === 1 ? singular : many}`;
}
