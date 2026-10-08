import { describe, expect, it } from 'vitest';
import {
  CURRENCY_SYMBOL,
  formatAuctionDateTime,
  formatCountdown,
  formatCurrency,
  formatDate,
  formatDateTime,
  formatNumber,
  formatOdometer,
  shortenDigests,
} from './format';

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

describe('formatCurrency', () => {
  it('renders whole Canadian dollars with grouping, the currency named on the figure', () => {
    expect(formatCurrency(22800)).toBe('CA$22,800');
  });

  it('names the same symbol on its own for a label or an input prefix', () => {
    expect(CURRENCY_SYMBOL).toBe('CA$');
    expect(formatCurrency(500).startsWith(CURRENCY_SYMBOL)).toBe(true);
  });
});

describe('formatOdometer', () => {
  it('renders grouped kilometres', () => {
    expect(formatOdometer(47731)).toBe('47,731 km');
  });
});

describe('formatCountdown', () => {
  const now = 1_000_000_000_000;

  it('renders days and hours when a day or more remains', () => {
    expect(formatCountdown(now + 2 * DAY + 4 * HOUR + 30 * MINUTE, now)).toBe('2d 4h');
  });

  it('renders hours and minutes under a day', () => {
    expect(formatCountdown(now + 3 * HOUR + 12 * MINUTE + 59 * SECOND, now)).toBe('3h 12m');
    expect(formatCountdown(now + 23 * HOUR + 59 * MINUTE, now)).toBe('23h 59m');
  });

  it('renders minutes and seconds under an hour', () => {
    expect(formatCountdown(now + 12 * MINUTE + 5 * SECOND, now)).toBe('12m 5s');
  });

  it('renders bare seconds under a minute', () => {
    expect(formatCountdown(now + 45 * SECOND, now)).toBe('45s');
  });

  it('renders "Ended" at and past the target', () => {
    expect(formatCountdown(now, now)).toBe('Ended');
    expect(formatCountdown(now - 1, now)).toBe('Ended');
  });
});

describe('shortenDigests', () => {
  const digest = 'sha256:40b891e5ea6b9a8a60c01942c563f8ea447578b002450cb6bf6ef6050a5f7a1c';

  it('keeps the first twelve characters, which is what every registry shows', () => {
    expect(shortenDigests(`Successfully pulled image "reg.azurecr.io/theyard@${digest}"`)).toBe(
      'Successfully pulled image "reg.azurecr.io/theyard@sha256:40b891e5ea6b\u2026"'
    );
  });

  it('leaves a message with no digest in it alone', () => {
    expect(shortenDigests('Killing container theyard (platform initiated).')).toBe(
      'Killing container theyard (platform initiated).'
    );
  });

  it('leaves a digest that is already short alone, rather than half-shortening it', () => {
    expect(shortenDigests('pulled sha256:40b891e5ea6b')).toBe('pulled sha256:40b891e5ea6b');
  });
});

describe('formatNumber', () => {
  it('groups thousands and keeps decimals, so one fact reads one way everywhere', () => {
    expect(formatNumber(1183)).toBe('1,183');
    expect(formatNumber(72541)).toBe('72,541');
    expect(formatNumber(298.4)).toBe('298.4');
    expect(formatNumber(12)).toBe('12');
  });
});

describe('formatDateTime', () => {
  it('writes a date and a time, and nothing for a bad date', () => {
    const when = Date.UTC(2026, 8, 25, 17, 4);
    expect(formatDateTime(when)).toBe(formatDateTime(new Date(when).toISOString()));
    expect(formatDateTime(when)).toMatch(/^Sep\.? 25, \d{1,2}:04/);
    expect(formatDateTime('not a date')).toBe('');
  });
});

describe('formatAuctionDateTime', () => {
  it('writes the date and time and then names the time zone of the viewer', () => {
    const when = Date.UTC(2026, 9, 2, 13, 43);
    const zone = new Intl.DateTimeFormat('en-CA', { timeZoneName: 'short' })
      .formatToParts(when)
      .find((part) => part.type === 'timeZoneName')?.value;
    const stamp = formatAuctionDateTime(when);
    expect(zone).toBeTruthy();
    expect(stamp.startsWith(formatDateTime(when))).toBe(true);
    expect(stamp.endsWith(` ${zone}`)).toBe(true);
  });

  it("writes UTC, whatever the viewer's zone, while a page drawn on a server is taken over", () => {
    const when = Date.UTC(2026, 9, 2, 13, 43);
    const stamp = formatAuctionDateTime(when, false);
    expect(stamp).toMatch(/1:43/);
    expect(stamp.endsWith(' UTC')).toBe(true);
  });
});

describe('formatDate', () => {
  it('writes a day with its year and no time, and nothing for a bad date', () => {
    const noonUtc = Date.UTC(2026, 8, 25, 12);
    expect(formatDate(noonUtc)).toMatch(/^Sep\.? 25, 2026$/);
    expect(formatDate(new Date(noonUtc).toISOString())).toBe(formatDate(noonUtc));
    expect(formatDate('not a date')).toBe('');
  });
});
