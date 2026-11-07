import { describe, expect, it } from 'vitest';
import {
  BUDGET_AMBER_DOLLARS,
  costTone,
  MONTHLY_BUDGET_DOLLARS,
  RANGES,
  rangeWords,
  spikeLine,
  spikeTone,
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
    expect(rangeWords('cost')).toBe('amber at $50, red at $60');
    expect(rangeWords('certificateDays')).toBe('amber under 30 days, red under 7 days');
    expect(rangeWords('clientErrorsHour')).toBe('amber at 50, red at 200');
  });

  it("holds the cost to Steve's budget of $60 a month, graded on what is being spent", () => {
    expect(MONTHLY_BUDGET_DOLLARS).toBe(60);
    expect(BUDGET_AMBER_DOLLARS).toBe(50);
    expect(RANGES.cost.red).toBe(60);
    // 4 November: $5.18 spent and $47.30 at yesterday's rate is under the amber line.
    expect(costTone(5.18, 47.3)).toBe('good');
    expect(costTone(5.18, 52)).toBe('warn');
    expect(costTone(5.18, 61)).toBe('bad');
    expect(costTone(61, null)).toBe('bad');
    expect(costTone(5.18, undefined)).toBe('good');
  });

  it("reads a day over three times the week's median as a spike, never under the $5 floor", () => {
    expect(spikeLine(1.45)).toBe(5);
    expect(spikeLine(2)).toBe(6);
    expect(spikeLine(null)).toBe(5);
    // 23 October, $24.99 over a $1.45 week: red whatever the month reads.
    expect(spikeTone({ cost: 24.99, median: 1.45 })).toBe('bad');
    expect(costTone(5.18, 30, [{ cost: 24.99, median: 1.45 }])).toBe('bad');
    // A $4.40 day over the same week is fine because of the floor.
    expect(spikeTone({ cost: 4.4, median: 1.45 })).toBe('good');
    expect(costTone(5.18, 30, [{ cost: 4.4, median: 1.45 }])).toBe('good');
    // Only the newest two days decide the tone; an older spike is history.
    const older = [
      { cost: 1.5, median: 1.45 },
      { cost: 1.4, median: 1.45 },
      { cost: 24.99, median: 1.45 },
    ];
    expect(costTone(5.18, 30, older)).toBe('good');
    expect(costTone(5.18, 30, [older[0], older[2]])).toBe('bad');
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
      cost: { amber: 50, red: 60, step: 0.01 },
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
    cost: ["The month, at yesterday's rate", '$'],
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
