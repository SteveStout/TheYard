import { describe, expect, it } from 'vitest';
import { formatCountdown, formatCurrency, formatOdometer } from './format';

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

describe('formatCurrency', () => {
  it('renders whole dollars with grouping', () => {
    expect(formatCurrency(22800)).toBe('$22,800');
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
