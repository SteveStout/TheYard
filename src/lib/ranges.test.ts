import { describe, expect, it } from 'vitest';
import {
  costTone,
  MONTHLY_BUDGET_DOLLARS,
  RANGES,
  rangeWords,
  toneOf,
  type Range,
  type RangeKey,
} from './ranges';

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

  // Every line by name and value, each side of it: a line that moves, or a range added without a row here, is red,
  // so a mutant that turns a >= into a > or nudges a number cannot pass unseen.
  const LINES: Record<RangeKey, { amber: number; red: number; step: number; lowIsBad?: boolean }> =
    {
      memory: { amber: 0.8, red: 0.95, step: 0.001 },
      p95: { amber: 1_000, red: 3_000, step: 1 },
      coldStart: { amber: 30_000, red: 60_000, step: 1 },
      ruMinute: { amber: 0.5, red: 0.9, step: 0.001 },
      sqlDtu: { amber: 0.6, red: 0.9, step: 0.001 },
      keptWarm: { amber: 1_000, red: 3_000, step: 1 },
      cost: { amber: 40, red: 50, step: 0.01 },
      clientErrorsHour: { amber: 50, red: 200, step: 1 },
      gateSeconds: { amber: 300, red: 600, step: 1 },
      certificateDays: { amber: 30, red: 7, step: 1, lowIsBad: true },
      sqlStorage: { amber: 0.8, red: 0.95, step: 0.001 },
    };

  const WORDS: Record<RangeKey, [string, string]> = {
    memory: ['Memory of its limit', '%'],
    p95: ['Ninety-fifth answer time', 'ms'],
    coldStart: ['Cold start, ready', 'ms'],
    ruMinute: ['Busiest minute of the free request units', '%'],
    sqlDtu: ['Azure SQL busiest share', '%'],
    keptWarm: ['Slowest kept-warm read', 'ms'],
    cost: ['The month, forecast', '$'],
    clientErrorsHour: ['Turned away (4xx) in the hour', ''],
    gateSeconds: ['The gate, start to verdict', 's'],
    certificateDays: ['Days left on the certificate', 'days'],
    sqlStorage: ['Azure SQL storage of its tier', '%'],
  };

  it('names every range and its unit as the page prints them', () => {
    for (const [key, [name, unit]] of Object.entries(WORDS) as [RangeKey, [string, string]][]) {
      const range: Range = RANGES[key];
      expect([key, range.name, range.unit]).toEqual([key, name, unit]);
    }
    expect(rangeWords('coldStart')).toBe('amber at 30 s, red at 60 s');
    expect(rangeWords('gateSeconds')).toBe('amber at 300 s, red at 600 s');
  });

  it('reads the pace from the month so far on its first day, and a forecast exactly at the pace as on it', () => {
    // Day 0: nothing to divide by, so the pace is what has been spent; a forecast past it by a tenth is ahead.
    expect(costTone(10, 12, 0, 31)).toBe('warn');
    expect(costTone(10, 10.5, 0, 31)).toBe('good');
    // Nothing spent and nothing forecast is on pace, not ahead of it.
    expect(costTone(0, 0, 10, 31)).toBe('good');
  });

  it('holds every range to its lines by name, with a row for each range', () => {
    expect(Object.keys(LINES).sort()).toEqual(Object.keys(RANGES).sort());
    for (const [key, line] of Object.entries(LINES) as [RangeKey, (typeof LINES)[RangeKey]][]) {
      const range: Range = RANGES[key];
      expect([key, range.amber, range.red, range.lowIsBad === true]).toEqual([
        key,
        line.amber,
        line.red,
        line.lowIsBad === true,
      ]);
    }
  });

  it('judges every range each side of its amber and its red line', () => {
    for (const [key, { amber, red, step, lowIsBad }] of Object.entries(LINES) as [
      RangeKey,
      (typeof LINES)[RangeKey],
    ][]) {
      const toward = lowIsBad === true ? -step : step;
      expect([key, toneOf(key, amber - toward)]).toEqual([key, 'good']);
      expect([key, toneOf(key, amber)]).toEqual([key, 'warn']);
      expect([key, toneOf(key, red - toward)]).toEqual([key, 'warn']);
      expect([key, toneOf(key, red)]).toEqual([key, 'bad']);
      expect([key, toneOf(key, red + toward)]).toEqual([key, 'bad']);
    }
  });
});
