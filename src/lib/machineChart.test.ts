import { describe, expect, it } from 'vitest';
import {
  axisLabel,
  ceilingFor,
  clockLabel,
  coverage,
  type KeptBucket,
  MACHINE_CHART,
  MACHINE_WINDOWS,
  pathFor,
  requestUnitsAMinute,
  shareOf,
  ticks,
  timeline,
  windowName,
} from './machineChart';

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

describe('kept windows', () => {
  const bucket = (at: string, over: Partial<KeptBucket> = {}): KeptBucket => ({
    at,
    minutes: 5,
    memory_limit_mb: 1000,
    working_set_mb: 300,
    working_set_max_mb: 320,
    managed_mb: 130,
    cpu_percent: 4,
    cpu_max_percent: 9,
    sql_cpu_percent: 1,
    sql_memory_percent: 40,
    sql_data_io_percent: 0,
    request_units: 10,
    operations: 4,
    ...over,
  });

  it('lays a window out slot by slot and leaves a slot the store does not hold empty', () => {
    const now = new Date('2026-09-20T12:03:00Z');
    const slots = timeline(
      [bucket('2026-09-20T11:55:00Z'), bucket('2026-09-20T11:45:00Z')],
      '24h',
      5,
      now
    );

    expect(slots).toHaveLength(288);
    // The last slot is the bucket now falls in, and the first is a day before it.
    expect(slots[287].at).toBe('2026-09-20T12:00:00.000Z');
    expect(slots[0].at).toBe('2026-09-19T12:05:00.000Z');
    expect(slots[286].bucket?.at).toBe('2026-09-20T11:55:00Z');
    // 11:50 was never kept: a gap, not a line drawn across it.
    expect(slots[285].bucket).toBeNull();
    expect(slots[284].bucket?.at).toBe('2026-09-20T11:45:00Z');
    expect(coverage(slots)).toEqual({ held: 2, of: 288 });
  });

  it('sizes the other two windows to a few hundred points', () => {
    const now = new Date('2026-09-20T12:03:00Z');
    expect(timeline([], '7d', 60, now)).toHaveLength(168);
    expect(timeline([], '30d', 240, now)).toHaveLength(180);
    expect(timeline([], '30d', 0, now)).toEqual([]);
  });

  it('reads memory as a share of its limit and refuses to divide by a limit it was not given', () => {
    expect(shareOf(300, 1200)).toBe(25);
    expect(shareOf(null, 1200)).toBeNull();
    expect(shareOf(300, 0)).toBeNull();
    expect(shareOf(300, null)).toBeNull();
  });

  it('charges a bucket over the minutes it holds, not the minutes it could have held', () => {
    expect(
      requestUnitsAMinute(bucket('2026-09-20T11:55:00Z', { request_units: 9, minutes: 3 }))
    ).toBe(3);
    expect(requestUnitsAMinute(null)).toBeNull();
    expect(requestUnitsAMinute(bucket('2026-09-20T11:55:00Z', { minutes: 0 }))).toBeNull();
  });

  it('labels a day by the clock and a month by the date', () => {
    const at = new Date(2026, 8, 14, 9, 5).toISOString();
    expect(axisLabel(at, '24h')).toBe('09:05');
    expect(axisLabel(at, '30d')).toBe('14 Sep');
    expect(axisLabel('not a date', '7d')).toBe('');
    expect(MACHINE_WINDOWS.map(windowName)).toEqual([
      'Last hour',
      'Last 24 hours',
      'Last 7 days',
      'Last 30 days',
    ]);
  });
});
