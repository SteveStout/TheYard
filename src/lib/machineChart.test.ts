import { describe, expect, it } from 'vitest';
import { ceilingFor, clockLabel, MACHINE_CHART, pathFor, ticks } from './machineChart';

const points = (values: (number | null)[]) =>
  values.map((value, index) => ({
    at: `2026-09-19T11:${String(index).padStart(2, '0')}:00Z`,
    value,
  }));

describe('ceilingFor', () => {
  it('rounds the top reading up to a number a person reads', () => {
    expect(ceilingFor([{ key: 'a', name: 'a', points: points([12, 48, 31]) }])).toBe(50);
    expect(ceilingFor([{ key: 'a', name: 'a', points: points([120, 260]) }])).toBe(500);
    expect(ceilingFor([{ key: 'a', name: 'a', points: points([0.4, 0.2]) }])).toBe(1);
  });

  it('keeps the floor it was given, so a quiet hour is not drawn as a busy one', () => {
    expect(ceilingFor([{ key: 'a', name: 'a', points: points([3, 4]) }], 100)).toBe(100);
    expect(ceilingFor([{ key: 'a', name: 'a', points: points([140]) }], 100)).toBe(200);
  });

  it('never returns zero, so an empty series still has an axis', () => {
    expect(ceilingFor([])).toBe(1);
    expect(ceilingFor([{ key: 'a', name: 'a', points: points([null, null]) }])).toBe(1);
  });
});

describe('pathFor', () => {
  it('draws one stroke across the readings it has', () => {
    const path = pathFor(points([0, 50, 100]), 100);
    expect(path.startsWith('M')).toBe(true);
    expect(path.match(/M/g)).toHaveLength(1);
    expect(path.match(/L/g)).toHaveLength(2);
    expect(path).toContain(`${MACHINE_CHART.left.toFixed(1)} `);
  });

  it('breaks the line at a missing reading rather than drawing across it', () => {
    const path = pathFor(points([10, null, 30]), 100);
    expect(path.match(/M/g)).toHaveLength(2);
    expect(path.match(/L/g)).toBeNull();
  });

  it('holds a reading above the ceiling to the top of the chart', () => {
    expect(pathFor(points([500]), 100)).toBe(
      `M${MACHINE_CHART.left.toFixed(1)} ${MACHINE_CHART.top.toFixed(1)}`
    );
  });
});

describe('ticks and labels', () => {
  it('labels the ends and the middle', () => {
    expect(ticks(0)).toEqual([]);
    expect(ticks(1)).toEqual([0]);
    expect(ticks(5)).toEqual([0, 2, 4]);
  });

  it('reads a time off the clock and says nothing about one it cannot read', () => {
    expect(clockLabel('2026-09-19T11:05:00Z')).toMatch(/^\d{2}:\d{2}$/);
    expect(clockLabel('not a time')).toBe('');
  });
});
