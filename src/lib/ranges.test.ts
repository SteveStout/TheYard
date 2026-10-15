import { describe, expect, it } from 'vitest';
import { costTone, MONTHLY_BUDGET_DOLLARS, RANGES, rangeWords, toneOf } from './ranges';

describe('ranges', () => {
  it('turns amber at the amber line and red at the red line', () => {
    expect(toneOf('memory', 0.79)).toBe('good');
    expect(toneOf('memory', 0.8)).toBe('warn');
    expect(toneOf('memory', 0.95)).toBe('bad');
    expect(toneOf('p95', 999)).toBe('good');
    expect(toneOf('p95', 3_000)).toBe('bad');
  });

  it('reads a range where less is worse the other way up', () => {
    expect(toneOf('certificateDays', 60)).toBe('good');
    expect(toneOf('certificateDays', 30)).toBe('warn');
    expect(toneOf('certificateDays', 6)).toBe('bad');
  });

  it('says every range in its own unit', () => {
    expect(rangeWords('memory')).toBe('amber at 80%, red at 95%');
    expect(rangeWords('p95')).toBe('amber at 1 s, red at 3 s');
    expect(rangeWords('cost')).toBe('amber at $40, red at $50');
    expect(rangeWords('certificateDays')).toBe('amber under 30 days, red under 7 days');
    expect(rangeWords('clientErrorsHour')).toBe('amber at 50, red at 200');
  });

  it("holds the cost to Steve's budget of $50 a month", () => {
    expect(MONTHLY_BUDGET_DOLLARS).toBe(50);
    expect(RANGES.cost.red).toBe(50);
    // On the 14th, $13.62 spent and $35.49 forecast: the pace reaches $30.16, so the forecast is ahead of it.
    expect(costTone(13.62, 35.49, 14, 31)).toBe('warn');
    expect(costTone(13.62, 30, 14, 31)).toBe('good');
    expect(costTone(20, 51, 14, 31)).toBe('bad');
    expect(costTone(52, null, 28, 31)).toBe('bad');
    expect(costTone(10, 42, 14, 31)).toBe('warn');
  });

  it('gives every range an amber line short of its red line and a source', () => {
    for (const range of Object.values(RANGES)) {
      const lowIsBad = 'lowIsBad' in range && range.lowIsBad === true;
      expect(lowIsBad ? range.amber > range.red : range.amber < range.red).toBe(true);
      expect(range.source.length).toBeGreaterThan(10);
    }
  });
});
