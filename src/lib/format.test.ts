import { describe, expect, it } from 'vitest';
import { formatCountdown, formatCurrency, formatOdometer, shortenDigests } from './format';

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
