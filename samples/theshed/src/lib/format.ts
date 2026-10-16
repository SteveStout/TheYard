/**
 * Formats raw numbers from the server for display. The server sends sizes in
 * bytes and times in milliseconds since the epoch, and leaves the display format
 * to the page. All formatting lives here, so the file table, the totals line and
 * the upload notice always show numbers the same way.
 * (More in docs/ADR-004-the-wire.md.)
 */

const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/**
 * Formats a byte count such as "1.2 MB". Plain bytes get no decimal and larger units
 * get one; digits are grouped with the person's locale separator. Each unit
 * is 1024 times the unit below it, matching how operating systems report file sizes.
 * Returns "" for a negative or non-finite count.
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
 * Formats milliseconds since the epoch as a short local date and time in the form
 * "YYYY-MM-DD HH:MM", or "today HH:MM" when the date is today.
 * Returns "" for zero, a negative or a non-finite value.
 * @param ms the time to format, in milliseconds since the epoch
 * @param now the current time; a parameter so a test can fix which day is "today"
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

/**
 * Formats a count with its noun, singular for exactly one: "1 folder", "12 files".
 * The plural defaults to the singular plus "s".
 */
export function plural(count: number, singular: string, many: string = `${singular}s`): string {
  return `${count.toLocaleString()} ${count === 1 ? singular : many}`;
}
