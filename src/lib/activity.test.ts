import { describe, expect, it } from 'vitest';
import {
  collectorSummary,
  costSentence,
  CHART,
  areaPath,
  ceilingOf,
  bandPath,
  countFor,
  dayLines,
  dayAt,
  labelSpot,
  partialDay,
  todayNote,
  pathShares,
  stackBands,
  stackCeiling,
  groupByDay,
  labelledIndexes,
  linePath,
  groupSources,
  namedPaths,
  pageName,
  sourceGroup,
  sortVisitors,
  type ActivityDay,
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

describe('unique visitors per day', () => {
  const days: ActivityDay[] = [
    {
      day: '2026-09-12',
      visitors: 3,
      humans: 2,
      bots: 1,
      people: 1,
      scanners: 1,
      self: 1,
      by_store: [
        { store: 'sql', visitors: 2, people: 1, scanners: 0, self: 1 },
        { store: 'cosmos', visitors: 1, people: 0, scanners: 1, self: 0 },
      ],
    },
    {
      day: '2026-09-13',
      visitors: 0,
      humans: 0,
      bots: 0,
      people: 0,
      scanners: 0,
      self: 0,
      by_store: [],
    },
  ];

  it('draws one line for everybody and one per store, on the same days', () => {
    const lines = dayLines(days, ['sql', 'cosmos'], 'all');
    expect(lines.map((line) => line.store)).toEqual(['all', 'sql', 'cosmos']);
    expect(lines[0].points.map((point) => point.requests)).toEqual([3, 0]);
    expect(lines[1].points.map((point) => point.requests)).toEqual([2, 0]);
    expect(lines[2].points.map((point) => point.requests)).toEqual([1, 0]);
    expect(lines[2].points[0].at).toBe('2026-09-12');
  });

  it('counts people only under Visitors only, on every line', () => {
    const lines = dayLines(days, ['sql', 'cosmos'], 'people');
    expect(lines[0].name).toBe('People');
    expect(lines[0].points.map((point) => point.requests)).toEqual([1, 0]);
    expect(lines[1].points.map((point) => point.requests)).toEqual([1, 0]);
    expect(lines[2].points.map((point) => point.requests)).toEqual([0, 0]);
  });

  it("stacks people, then scanners, then the site's own reads, each band on the one below", () => {
    const bands = stackBands(days, 'all');
    expect(bands.map((band) => band.kind)).toEqual(['people', 'scanners', 'self']);
    expect(bands[0].lower).toEqual([0, 0]);
    expect(bands[0].upper).toEqual([1, 0]);
    expect(bands[1].lower).toEqual([1, 0]);
    expect(bands[1].upper).toEqual([2, 0]);
    expect(bands[2].upper).toEqual([3, 0]);
    expect(stackCeiling(bands)).toBe(3);
  });

  it('stacks the people alone under Visitors only, and a quiet window still has an axis', () => {
    const bands = stackBands(days, 'people');
    expect(bands.map((band) => band.kind)).toEqual(['people']);
    expect(stackCeiling(stackBands([days[1]], 'all'))).toBe(1);
  });

  it('closes a band along its floor and names it only where it is as thick as a line of text', () => {
    const [people, scanners] = stackBands(days, 'all');
    const d = bandPath(people, 3);
    expect(d.startsWith('M')).toBe(true);
    expect(d.endsWith('Z')).toBe(true);
    expect(d.split(/[ML]/).length - 1).toBe(4);
    const spot = labelSpot(scanners, 3);
    expect(spot).not.toBeNull();
    expect(spot?.anchor).toBe('start');
    expect(labelSpot(scanners, 3, 1000)).toBeNull();
  });

  it("draws each step of the recruiter's path as a share of the widest, never thinner than a sliver once reached", () => {
    expect(
      pathShares([
        { visitor_days: 400 },
        { visitor_days: 100 },
        { visitor_days: 1 },
        { visitor_days: 0 },
      ])
    ).toEqual([1, 0.25, 0.02, 0]);
    expect(pathShares([{ visitor_days: 0 }, { visitor_days: 0 }])).toEqual([0, 0]);
  });

  it('says the last day is a part-day, with its hours, only when it is today', () => {
    const now = new Date('2026-09-13T16:20:00Z');
    expect(partialDay(days, now)).toEqual({ index: 1, hours: 16 });
    expect(partialDay(days, new Date('2026-09-14T01:00:00Z'))).toBeNull();
    expect(partialDay([], now)).toBeNull();
    expect(todayNote(16)).toBe('today, 16 h in');
  });

  it('reads the day under a pointer, the nearest point, never off either end', () => {
    expect(dayAt(CHART.left, 8)).toBe(0);
    expect(dayAt(CHART.width - CHART.right, 8)).toBe(7);
    expect(dayAt(-50, 8)).toBe(0);
    expect(dayAt(10_000, 8)).toBe(7);
    expect(dayAt(123, 1)).toBe(0);
  });

  it('names a path by the page a person asked for, the most particular pattern first', () => {
    expect(pageName('/')).toBe('Opened the site');
    expect(pageName('/index.html')).toBe('Opened the site');
    expect(pageName('/api/vehicles')).toBe('Inventory listing');
    expect(pageName('/api/vehicles/abc-001/bids')).toBe("One vehicle's bids");
    expect(pageName('/api/vehicles/abc-001')).toBe('One vehicle');
    expect(pageName('/api/docs/resume')).toBe('The resume');
    expect(pageName('/docs/resume.pdf')).toBe('The resume');
    expect(pageName('/resume.pdf')).toBe('The resume');
    expect(pageName('/api/docs/changelog')).toBe('Document: changelog');
    expect(pageName('/wp-admin/install.php')).toBe('/wp-admin/install.php');
  });

  it('adds two paths that are one page, most asked for first', () => {
    expect(
      namedPaths([
        { path: '/api/facets', requests: 5 },
        { path: '/index.html', requests: 4 },
        { path: '/', requests: 3 },
      ])
    ).toEqual([
      { name: 'Opened the site', requests: 7 },
      { name: 'Facets and filters', requests: 5 },
    ]);
  });

  it('puts a referring host in one of five groups, a host nothing names being another site', () => {
    expect(sourceGroup('www.linkedin.com')).toBe('linkedin');
    expect(sourceGroup('lnkd.in')).toBe('linkedin');
    expect(sourceGroup('github.com')).toBe('github');
    expect(sourceGroup('stevestout.github.io')).toBe('github');
    expect(sourceGroup('www.google.com')).toBe('search');
    expect(sourceGroup('www.google.co.uk')).toBe('search');
    expect(sourceGroup('duckduckgo.com')).toBe('search');
    expect(sourceGroup('boards.greenhouse.io')).toBe('other');
    expect(sourceGroup('notlinkedin.com')).toBe('other');
    expect(sourceGroup('(none)')).toBe('none');
  });

  it('adds the hosts up by group, every group present and in the fixed order', () => {
    expect(
      groupSources([
        { host: 'www.linkedin.com', visitor_days: 9 },
        { host: 'lnkd.in', visitor_days: 3 },
        { host: '(none)', visitor_days: 18 },
      ])
    ).toEqual([
      { group: 'linkedin', visitor_days: 12 },
      { group: 'github', visitor_days: 0 },
      { group: 'search', visitor_days: 0 },
      { group: 'other', visitor_days: 0 },
      { group: 'none', visitor_days: 18 },
    ]);
  });

  it('adds the three kinds under All traffic, and only people under Visitors only', () => {
    const kinds = { people: 4, scanners: 7, self: 2 };
    expect(countFor(kinds, 'all')).toBe(13);
    expect(countFor(kinds, 'people')).toBe(4);
  });

  it('groups the visitor rows by day, newest first, counting a token once per day', () => {
    const row = (
      day: string,
      visitor: string,
      store: string,
      requests: number
    ): ActivityVisitor => ({
      visitor,
      network: '203.0.113.x',
      store,
      day,
      first_seen: `${day}T01:00:00Z`,
      last_seen: `${day}T02:00:00Z`,
      requests,
      bots: 0,
      top_paths: [],
    });
    const grouped = groupByDay([
      row('2026-09-12', 'a', 'sql', 4),
      row('2026-09-13', 'b', 'cosmos', 1),
      row('2026-09-12', 'a', 'cosmos', 2),
    ]);
    expect(grouped.map((group) => group.day)).toEqual(['2026-09-13', '2026-09-12']);
    expect(grouped[1].visitors).toBe(1);
    expect(grouped[1].requests).toBe(6);
    expect(grouped[1].rows).toHaveLength(2);
  });
});

describe('the collector in words', () => {
  const collector = {
    offered: 10,
    written: 10,
    failed_batches: 0,
    dropped: 0,
    last_write: null,
    interval_seconds: 5,
  };

  it('says fine, or what went wrong first, in the right number', () => {
    expect(collectorSummary(collector)).toBe('Collector fine');
    expect(collectorSummary({ ...collector, failed_batches: 1, dropped: 4 })).toBe(
      'Collector: 1 batch failed'
    );
    expect(collectorSummary({ ...collector, failed_batches: 3 })).toBe(
      'Collector: 3 batches failed'
    );
    expect(collectorSummary({ ...collector, dropped: 1 })).toBe('Collector: 1 hit dropped');
    expect(collectorSummary({ ...collector, dropped: 1200 })).toBe('Collector: 1,200 hits dropped');
  });

  it('says whose the cost is: this container, since it started', () => {
    expect(costSentence({ request_units: 1234.5, operations: 80, failures: 0 })).toBe(
      'Keeping it has cost this container 1,234.5 request units over 80 operations since it started.'
    );
    expect(costSentence({ request_units: 2, operations: 3, failures: 1 })).toMatch(
      /, 1 of them failed\.$/
    );
  });
});
