import { describe, expect, it } from 'vitest';
import {
  axisLabel,
  ceilingFor,
  clockLabel,
  coverage,
  hourOfTraffic,
  fromFirstReading,
  type KeptBucket,
  keptSparks,
  keptTraffic,
  LEAST_SLOTS,
  MACHINE_CHART,
  MACHINE_WINDOWS,
  pairedBars,
  pathFor,
  requestUnitsAMinute,
  shareOf,
  markTicks,
  peakOf,
  nearestIndex,
  readoutLine,
  ticks,
  timeline,
  trafficTotals,
  windowName,
  xOf,
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
    requests: 20,
    p50_ms: 8,
    p95_ms: 60,
    server_errors: 0,
    client_errors: 5,
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

  it('starts a drawing at the first reading, and leaves every later gap where it is', () => {
    const now = new Date('2026-09-20T12:03:00Z');
    const month = timeline(
      [bucket('2026-09-18T08:00:00Z'), bucket('2026-09-20T08:00:00Z')],
      '30d',
      240,
      now
    );
    const drawn = fromFirstReading(month);
    // Two days and a bit of four-hour buckets, from the first reading to now, the gap between the two still in it.
    expect(drawn[0].bucket?.at).toBe('2026-09-18T08:00:00Z');
    expect(drawn).toHaveLength(14);
    expect(drawn.filter((slot) => slot.bucket === null)).toHaveLength(12);
    expect(drawn[drawn.length - 1].at).toBe(month[month.length - 1].at);
    // What the window holds is still counted against the whole window.
    expect(coverage(month)).toEqual({ held: 2, of: 180 });
    // A record an hour old keeps a dozen slots, and a window that holds nothing is left whole.
    const young = timeline([bucket('2026-09-20T12:00:00Z')], '24h', 5, now);
    expect(fromFirstReading(young)).toHaveLength(LEAST_SLOTS);
    expect(fromFirstReading(timeline([], '7d', 60, now))).toHaveLength(168);
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

  it('draws the hour from the oldest request the ring holds, with true zeros in the quiet minutes', () => {
    const asOf = new Date('2026-09-20T12:03:30Z');
    const minute = (at: string, requests: number) => ({
      at,
      requests,
      p50_ms: 4,
      p95_ms: 30,
      server_errors: 0,
      client_errors: 1,
    });
    const slots = hourOfTraffic(
      [minute('2026-09-20T12:00:00Z', 6), minute('2026-09-20T12:02:00Z', 2)],
      asOf
    );

    expect(slots.map((slot) => slot.at)).toEqual([
      '2026-09-20T12:00:00.000Z',
      '2026-09-20T12:01:00.000Z',
      '2026-09-20T12:02:00.000Z',
      '2026-09-20T12:03:00.000Z',
    ]);
    // 12:01 was measured and nobody came: zero requests, and no median of nothing.
    expect(slots[1]).toMatchObject({ requests: 0, p50_ms: null, p95_ms: null });
    expect(slots[0]).toMatchObject({ requests: 6, p50_ms: 4, client_errors: 1 });
    // A ring that reaches back further than an hour is drawn as an hour.
    expect(hourOfTraffic([minute('2026-09-20T09:00:00Z', 1)], asOf)).toHaveLength(60);
    expect(hourOfTraffic([], asOf)).toHaveLength(1);
  });

  it('turns a kept bucket into a rate a minute and leaves a bucket nobody kept as a gap', () => {
    const slots = keptTraffic([
      {
        at: 'a',
        bucket: bucket('2026-09-20T11:55:00Z', { requests: 20, minutes: 4, client_errors: 2 }),
      },
      { at: 'b', bucket: null },
    ]);

    expect(slots[0]).toMatchObject({ requests: 5, client_errors: 0.5, p50_ms: 8, p95_ms: 60 });
    expect(slots[1]).toEqual({
      at: 'b',
      requests: null,
      p50_ms: null,
      p95_ms: null,
      server_errors: null,
      client_errors: null,
    });
    expect(trafficTotals(slots, 4)).toEqual({
      requests: 20,
      server_errors: 0,
      client_errors: 2,
      slowest_p95_ms: 60,
      slowest_at: 'a',
    });
  });

  it('hands the tiles a kept window as four lines, with a gap where the store holds nothing', () => {
    const lines = keptSparks([
      { at: 'a', bucket: bucket('2026-09-20T11:55:00Z', { p95_ms: 60, server_errors: 2 }) },
      { at: 'b', bucket: null },
      { at: 'c', bucket: bucket('2026-09-20T12:05:00Z', { p50_ms: null, working_set_mb: 410 }) },
    ]);
    // The speed line is the typical answer, the same reading as the number over it.
    expect(lines.speed).toEqual([8, null, null]);
    expect(lines.memory).toEqual([300, null, 410]);
    expect(lines.charged).toEqual([10, null, 10]);
    expect(lines.errors).toEqual([2, null, 0]);
  });

  it('draws the proof as pairs of bars against the longest median, and a zero as a sliver', () => {
    const bars = pairedBars([
      {
        label: 'Bid write',
        cells: [
          { store: 'Azure SQL Database', samples: 8, p50_ms: 28 },
          { store: 'Azure Cosmos DB', samples: 8, p50_ms: 89 },
        ],
      },
      {
        label: 'Vehicle page',
        cells: [
          { store: 'Azure SQL Database', samples: 8, p50_ms: 0 },
          { store: 'Azure Cosmos DB', samples: 8, p50_ms: 0 },
        ],
      },
      { label: 'Register', cells: [{ store: 'Azure SQL Database', samples: 0, p50_ms: 0 }] },
    ]);

    expect(bars).toHaveLength(2);
    expect(bars[0].bars.map((bar) => bar.share)).toEqual([31, 100]);
    expect(bars[1].bars.map((bar) => bar.share)).toEqual([1, 1]);
  });
});

describe('the readout on a chart', () => {
  it("reads the slot nearest the pointer, in the drawing's own units", () => {
    // 61 slots across 664 units from 44: a slot every 11.07.
    expect(nearestIndex(44, 61)).toBe(0);
    expect(nearestIndex(44 + 11.07 * 30, 61)).toBe(30);
    expect(nearestIndex(44 + 11.07 * 30 + 5, 61)).toBe(30);
    expect(nearestIndex(44 + 11.07 * 30 + 6, 61)).toBe(31);
  });

  it('clamps to the plot, so the edge of the card reads the first or the last slot', () => {
    expect(nearestIndex(0, 61)).toBe(0);
    expect(nearestIndex(720, 61)).toBe(60);
  });

  it('has nothing to read on an empty chart, and one thing on a chart of one', () => {
    expect(nearestIndex(100, 0)).toBeNull();
    expect(nearestIndex(100, 1)).toBe(0);
  });

  it('puts the rule where the line puts the point', () => {
    expect(xOf(0, 61)).toBe(MACHINE_CHART.left);
    expect(xOf(60, 61)).toBe(MACHINE_CHART.width - MACHINE_CHART.right);
    expect(xOf(0, 1)).toBe(MACHINE_CHART.left);
  });

  // The tweaks pass (B2): graduations on the axes where the grid was.
  it('graduates both axes outside the plot, the quarters up the side and the labelled slots along', () => {
    const marks = markTicks(61);
    const up = marks.filter((tick) => tick.key.startsWith('y'));
    expect(up.map((tick) => tick.y1)).toEqual([146, 112.5, 79, 45.5, 12]);
    expect(up.filter((tick) => tick.major).map((tick) => tick.x2 - tick.x1)).toEqual([6, 6, 6]);
    expect(up.filter((tick) => !tick.major).map((tick) => tick.x2 - tick.x1)).toEqual([3, 3]);
    const along = marks.filter((tick) => !tick.key.startsWith('y'));
    expect(along.filter((tick) => tick.major)).toHaveLength(3);
    expect(along.filter((tick) => !tick.major)).toHaveLength(9);
    // No tick crosses into the plot: each one starts at its axis and stands outside it.
    for (const tick of up) expect(tick.x2).toBe(MACHINE_CHART.left);
    for (const tick of along) expect(tick.y1).toBe(MACHINE_CHART.height - MACHINE_CHART.bottom);
  });

  it('finds the peak to call out, and none when nothing rose above zero', () => {
    const points = [0, 2, 11, 3, null].map((value, index) => ({
      at: `2026-09-25T06:0${index}:00Z`,
      value,
    }));
    const peak = peakOf(points, 12);
    expect(peak).toMatchObject({ index: 2, value: 11 });
    expect(peak?.x).toBe(xOf(2, 5));
    expect(
      peakOf(
        points.map((point) => ({ ...point, value: 0 })),
        12
      )
    ).toBeNull();
  });

  it('says a reading in the unit the axis is in, and says a gap is a gap', () => {
    expect(readoutLine('Slow requests (95th percentile)', 1310, 'ms')).toBe(
      'Slow requests (95th percentile): 1,310 ms'
    );
    expect(readoutLine('Memory, share of the limit', 33, '%')).toBe(
      'Memory, share of the limit: 33%'
    );
    expect(readoutLine('Requests', 4)).toBe('Requests: 4');
    expect(readoutLine('Requests', null, 'requests / min')).toBe('Requests: not measured');
  });
});
