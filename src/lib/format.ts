/**
 * All user-facing formatting. Currency display is driven by the constants
 * below, so moving the app to another currency or locale is a one-line change.
 * CAD is the default because every listing in the dataset is Canadian.
 */
export const CURRENCY = 'CAD';
export const LOCALE = 'en-CA';

const SECOND_MS = 1000;
const MINUTE_MS = 60 * SECOND_MS;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/**
 * The locale prices are written in. en-CA writes Canadian dollars as a bare
 * "$", which a reader outside Canada takes for US dollars. en-US writes the
 * same currency as "CA$", so the unit is on every price the site shows.
 */
const CURRENCY_LOCALE = 'en-US';

const currencyFormat = new Intl.NumberFormat(CURRENCY_LOCALE, {
  style: 'currency',
  currency: CURRENCY,
  maximumFractionDigits: 0,
});

/** "CA$22,800", whole dollars only, the currency named on the figure. */
export function formatCurrency(amount: number): string {
  return currencyFormat.format(amount);
}

/**
 * "CA$", the symbol formatCurrency puts in front of a price, for the places
 * that name the currency without a figure: the price filter's label and the
 * prefix inside the bid box. Read from the same formatter, so the two cannot
 * disagree.
 */
export const CURRENCY_SYMBOL =
  currencyFormat.formatToParts(0).find((part) => part.type === 'currency')?.value ?? CURRENCY;

const integerFormat = new Intl.NumberFormat(LOCALE);

/** "100,000", grouped whole number. */
export function formatInteger(value: number): string {
  return integerFormat.format(value);
}

/**
 * "1,183" or "298.4": a reading grouped by thousands, decimals kept. Every
 * number shown with a unit (ms, MB, RU) goes through here, so the same fact
 * never reads as "1183" in one place and "1,183" in another.
 */
export function formatNumber(value: number): string {
  return integerFormat.format(value);
}

/** "47,731 km" */
export function formatOdometer(km: number): string {
  return `${integerFormat.format(km)} km`;
}

/** "sedan" → "Sedan"; leaves already-capitalized values (CVT, 4WD) alone. */
export function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

const dateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
});

/**
 * The auction stamp names the time zone it is written in. Auctions turn over
 * at midnight UTC, which is evening in North America, so an end time with no
 * zone on it reads as a different hour to every visitor who assumes another
 * zone. The stamp stays in the viewer's own zone and says which one that is.
 */
const auctionDateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
  timeZoneName: 'short',
});

/** "Apr. 5, 2:00 p.m. CDT", for auction start and end stamps, in the viewer's zone. */
export function formatAuctionDateTime(epochMs: number): string {
  return auctionDateTimeFormat.format(epochMs);
}

/**
 * "Apr. 5, 2:00 p.m." for any moment the site states with its date: a test
 * run, a page sweep, a visitor's first and last visit. One format for a date
 * and a time, wherever it appears outside an auction.
 */
export function formatDateTime(when: number | string): string {
  const date = new Date(when);
  return Number.isNaN(date.getTime()) ? '' : dateTimeFormat.format(date);
}

const dateFormat = new Intl.DateTimeFormat(LOCALE, {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});

/**
 * "Apr. 5, 2026", a day with no time on it, such as the day an account was
 * opened. The same locale as every other date, rather than the browser's
 * default, so one site writes its dates one way. Empty for a bad date.
 */
export function formatDate(when: number | string): string {
  const date = new Date(when);
  return Number.isNaN(date.getTime()) ? '' : dateFormat.format(date);
}

/**
 * Compact countdown to `target`: "2d 4h", "3h 12m", "12m 5s", "45s",
 * or "Ended" once the target has passed. Callers pick the target: an
 * auction's end while live, its start while upcoming.
 */
export function formatCountdown(target: number, now: number): string {
  const remaining = target - now;
  if (remaining <= 0) return 'Ended';
  const days = Math.floor(remaining / DAY_MS);
  const hours = Math.floor((remaining % DAY_MS) / HOUR_MS);
  const minutes = Math.floor((remaining % HOUR_MS) / MINUTE_MS);
  const seconds = Math.floor((remaining % MINUTE_MS) / SECOND_MS);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

/**
 * Azure's container events quote the image by digest, and a digest is
 * `sha256:` followed by sixty-four hexadecimal characters. Printed whole in a
 * card three hundred pixels wide it wraps into three lines of noise, and it
 * breaks after "sha" so the next line opens with "256:", which reads as a
 * number rather than as the tail of a name.
 *
 * Twelve characters is what every registry and every `docker images` output
 * shows, and it is enough to tell two builds apart, which is the only reason
 * the digest is on the page at all.
 */
export function shortenDigests(message: string): string {
  return message.replace(/\b(sha256:)([0-9a-f]{12})[0-9a-f]{52}\b/g, '$1$2\u2026');
}
