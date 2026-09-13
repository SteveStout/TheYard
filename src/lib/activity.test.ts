import { describe, expect, it } from 'vitest';
import {
  CHART,
  areaPath,
  ceilingOf,
  labelledIndexes,
  linePath,
  sortVisitors,
  type ActivitySeries,
  type ActivityVisitor,
} from './activity';

/**
 * The activity card's arithmetic (ADR: Site activity, and the line an address
 * does not cross). What is worth holding: a flat day still has an axis, the
 * line spans the drawing area and never leaves it, the area closes to the
 * baseline, the x labels always include the ends, and the table sorts both
 * ways on any column.
 */

const series: ActivitySeries[] = [
  {
    store: 'sql',
    name: 'Azure SQL Database',
    points: [
      { at: '2026-09-13T00:00:00Z', requests: 0, bots: 0 },
      { at: '2026-09-13T01:00:00Z', requests: 4, bots: 1 },
      { at: '2026-09-13T02:00:00Z', requests: 10, bots: 3 },
    ],
  },
  {
    store: 'cosmos',
    name: 'Azure Cosmos DB',
    points: [
      { at: '2026-09-13T00:00:00Z', requests: 2, bots: 0 },
      { at: '2026-09-13T01:00:00Z', requests: 0, bots: 0 },
      { at: '2026-09-13T02:00:00Z', requests: 5, bots: 5 },
    ],
  },
];

describe('the graph', () => {
  it('has a ceiling of at least one, so a quiet day still draws an axis', () => {
    expect(ceilingOf([])).toBe(1);
    expect(ceilingOf([{ store: 'sql', name: '', points: [] }])).toBe(1);
    expect(ceilingOf(series)).toBe(10);
  });

  it('draws the line from the left edge to the right edge and inside the area', () => {
    const d = linePath(series[0].points, 10);
    const numbers = d.match(/-?\d+(\.\d+)?/g)!.map(Number);
    const xs = numbers.filter((_, index) => index % 2 === 0);
    const ys = numbers.filter((_, index) => index % 2 === 1);
    expect(d.startsWith('M')).toBe(true);
    expect(xs[0]).toBe(CHART.left);
    expect(xs[xs.length - 1]).toBe(CHART.width - CHART.right);
    for (const y of ys) {
      expect(y).toBeGreaterThanOrEqual(CHART.top);
      expect(y).toBeLessThanOrEqual(CHART.height - CHART.bottom);
    }
    // The highest point sits on the top line, and a zero sits on the baseline.
    expect(ys[2]).toBe(CHART.top);
    expect(ys[0]).toBe(CHART.height - CHART.bottom);
  });

  it('closes the area down to the baseline and back to the left edge', () => {
    const d = areaPath(series[1].points, 10);
    expect(d.endsWith(`L${CHART.left} ${CHART.height - CHART.bottom} Z`)).toBe(true);
    expect(areaPath([], 10)).toBe('');
  });

  it('labels the first and the last point whatever the count', () => {
    expect(labelledIndexes(0)).toEqual([]);
    expect(labelledIndexes(1)).toEqual([0]);
    expect(labelledIndexes(24)).toContain(0);
    expect(labelledIndexes(24)).toContain(23);
    expect(labelledIndexes(168)).toContain(167);
    expect(labelledIndexes(24).length).toBeLessThanOrEqual(7);
  });
});

describe('the visitor table', () => {
  const rows: ActivityVisitor[] = [
    {
      visitor: 'a',
      network: '203.0.113.x',
      store: 'sql',
      day: '2026-09-13',
      first_seen: '2026-09-13T01:00:00Z',
      last_seen: '2026-09-13T02:00:00Z',
      requests: 3,
      bots: 0,
      top_paths: [],
    },
    {
      visitor: 'b',
      network: '198.51.100.x',
      store: 'cosmos',
      day: '2026-09-13',
      first_seen: '2026-09-13T00:30:00Z',
      last_seen: '2026-09-13T03:00:00Z',
      requests: 12,
      bots: 12,
      top_paths: [],
    },
  ];

  it('sorts newest first by default and the other way on request', () => {
    expect(sortVisitors(rows, 'last_seen', true).map((row) => row.visitor)).toEqual(['b', 'a']);
    expect(sortVisitors(rows, 'last_seen', false).map((row) => row.visitor)).toEqual(['a', 'b']);
  });

  it('sorts numbers as numbers and text as text', () => {
    expect(sortVisitors(rows, 'requests', true)[0].visitor).toBe('b');
    expect(sortVisitors(rows, 'network', false)[0].network).toBe('198.51.100.x');
  });

  it('does not change the rows it was given', () => {
    const before = rows.map((row) => row.visitor);
    sortVisitors(rows, 'requests', true);
    expect(rows.map((row) => row.visitor)).toEqual(before);
  });
});
