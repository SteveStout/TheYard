/**
 * All user-facing formatting. Currency display is driven by the constants
 * below — moving the app to another currency/locale is a one-line change.
 * CAD is the default because every listing in the dataset is Canadian.
 */
export const CURRENCY = 'CAD';
export const LOCALE = 'en-CA';

const SECOND_MS = 1000;
const MINUTE_MS = 60 * SECOND_MS;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

const currencyFormat = new Intl.NumberFormat(LOCALE, {
  style: 'currency',
  currency: CURRENCY,
  maximumFractionDigits: 0,
});

/** "$22,800" — whole dollars only. */
export function formatCurrency(amount: number): string {
  return currencyFormat.format(amount);
}

const integerFormat = new Intl.NumberFormat(LOCALE);

/** "100,000" — grouped whole number. */
export function formatInteger(value: number): string {
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

/** "Apr. 5, 2:00 p.m." — for auction start/end stamps. */
export function formatAuctionDateTime(epochMs: number): string {
  return dateTimeFormat.format(epochMs);
}

/**
 * Compact countdown to `target`: "2d 4h", "3h 12m", "12m 5s", "45s",
 * or "Ended" once the target has passed. Callers pick the target — an
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
