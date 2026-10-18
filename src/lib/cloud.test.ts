import { describe, expect, it } from 'vitest';
import {
  angleOf,
  arcPath,
  dayMarks,
  linePath,
  ticks,
  toneWord,
  valueLines,
  xOf,
  yOf,
} from './cloud';

describe('the Cloud infrastructure frame', () => {
  it('fills the dial clockwise from twelve o’clock and never closes the ring', () => {
    expect(angleOf(0)).toBe(-90);
    expect(angleOf(25)).toBe(0);
    expect(angleOf(150)).toBeCloseTo(269.964, 2);
    expect(arcPath(100, 100, 50, 0, 75)).toContain(' 1 1 ');
    expect(ticks()).toHaveLength(40);
    expect(ticks().filter((tick) => tick.long)).toHaveLength(10);
  });

  it('breaks a line over a minute nobody read rather than drawing a zero', () => {
    expect(
      linePath([
        { x: 0, y: 10 },
        { x: 1, y: null },
        { x: 2, y: 20 },
      ])
    ).toBe('M 0.0 10.0 M 2.0 20.0');
    expect(yOf(100, 50)).toBe(0);
    expect(yOf(-5, 50)).toBe(50);
    expect(xOf(5, 0, 10, 100)).toBe(50);
  });

  it('names only Mondays and Fridays along the axis', () => {
    const start = Date.UTC(2026, 9, 12);
    const marks = dayMarks(start, start + 7 * 86_400_000);
    expect(marks.filter((mark) => mark.label !== null).map((mark) => mark.label)).toEqual([
      'Mon',
      'Fri',
      'Mon',
    ]);
  });

  it('says what a tone means in the owner’s words', () => {
    expect(toneWord('good')).toBe('fine');
    expect(toneWord('info')).toBe('for information');
    expect(toneWord('alarm')).toBe('warning');
  });

  it('prints a listed readout value one item a line, and keeps a thousands comma whole', () => {
    expect(
      valueLines('stevenstout.biz, theyard-cosmos.stevenstout.biz, www.stevenstout.biz')
    ).toEqual(['stevenstout.biz', 'theyard-cosmos.stevenstout.biz', 'www.stevenstout.biz']);
    expect(valueLines('2,152')).toEqual(['2,152']);
  });
});
