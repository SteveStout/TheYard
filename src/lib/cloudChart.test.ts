import { describe, expect, it } from 'vitest';
import {
  bandsShown,
  dayLabels,
  fullLines,
  joinClose,
  legendRest,
  lineLabel,
  markKey,
  nowLabel,
  panelsOf,
  peakBand,
  spaced,
  ticksOf,
  windowOf,
  type Chart,
  type ChartMark,
} from './cloudChart';

const HOUR = 3_600_000;
const DAY = 86_400_000;
// Monday 26 October 2026, noon UTC, and the week before it.
const NOW = Date.UTC(2026, 9, 26, 12);
const WEEK_START = NOW - 7 * DAY;

/** The plan card's chart as the server sends it after the wire carries each series' own lines. */
function planChart(marks: ChartMark[] = []): Chart {
  const points = (value: number, peak: number) =>
    Array.from({ length: 168 }, (_, i) => ({ at_ms: WEEK_START + i * HOUR, value, peak }));
  return {
    title: 'CPU and memory, 7 days',
    series: [
      { name: 'CPU', points: points(6, 40), amber: 60, red: 80 },
      { name: 'Memory', points: points(57, 59), amber: 70, red: 85 },
    ],
    marks,
    red_line: 80,
    legend:
      'teal CPU · green memory · gold deploys, shaded while the roll runs · red Azure restart · dashed 80 % · kept since Tue 20',
    start_ms: WEEK_START,
    bands: [{ start_ms: NOW - 2 * HOUR, end_ms: NOW - HOUR, title: 'Deploy 1.0.4.92' }],
  };
}

const deploy = (at: number, v: string): ChartMark => ({
  at_ms: at,
  kind: 'deploy',
  title: `Deployed ${v}`,
});

describe('one threshold, one line', () => {
  it('draws two series with different lines as two panels, each against its own amber and red', () => {
    const panels = panelsOf(planChart());
    expect(panels.map((p) => [p.name, p.amber, p.red])).toEqual([
      ['CPU', 60, 80],
      ['Memory', 70, 85],
    ]);
    expect(panels.every((p) => p.series.length === 1)).toBe(true);
  });

  it('keeps a chart whose series carry no lines as one panel against the chart’s red line', () => {
    const chart = planChart();
    chart.series = chart.series.map(({ name, points }) => ({ name, points }));
    const panels = panelsOf(chart);
    expect(panels).toHaveLength(1);
    expect(panels[0].red).toBe(80);
    expect(panels[0].series).toHaveLength(2);
  });

  it('labels each line as the margin prints it', () => {
    expect(lineLabel('amber', 60)).toBe('amber 60');
    expect(lineLabel('red', 85)).toBe('red 85');
    expect(lineLabel('red', 12.5)).toBe('red 12.5');
  });
});

describe('rolls on the wider windows', () => {
  const marks = [
    deploy(NOW - 3 * DAY, '1.0.4.70'),
    deploy(NOW - 3 * DAY + 20 * 60_000, '1.0.4.71'),
    deploy(NOW - 3 * DAY + 70 * 60_000, '1.0.4.72'),
    deploy(NOW - DAY, '1.0.4.80'),
    { at_ms: NOW - 2 * DAY, kind: 'event' as const, title: 'Restart Web App on APP, by Azure' },
  ];

  it('makes rolls within an hour of each other one counted tick at seven and thirty days', () => {
    for (const window of ['7d', '30d']) {
      const ticks = ticksOf(marks, window);
      expect(ticks.map((t) => [t.kind, t.count])).toEqual([
        ['deploy', 3],
        ['deploy', 1],
      ]);
      expect(ticks[0].titles).toContain('Deployed 1.0.4.71');
      // A roll is not a full-height line there, and its shading is not drawn.
      expect(fullLines(marks, window).map((m) => m.kind)).toEqual(['event']);
      expect(bandsShown(window)).toBe(false);
    }
  });

  it('keeps each roll a full-height line with its shading at an hour and a day', () => {
    for (const window of ['1h', '24h']) {
      expect(ticksOf(marks, window)).toEqual([]);
      expect(fullLines(marks, window).filter((m) => m.kind === 'deploy')).toHaveLength(4);
      expect(bandsShown(window)).toBe(true);
    }
  });

  it('keeps an Azure restart a full-height line at every window, because it is rare and it matters', () => {
    for (const window of ['24h', '7d', '30d']) {
      expect(fullLines(marks, window).some((m) => m.kind === 'event')).toBe(true);
    }
  });

  it('draws a bucket’s tally as a tick at every window', () => {
    const tallies: ChartMark[] = [
      { at_ms: NOW - 5 * HOUR, kind: 'count', title: '8 of the site’s requests throttled (429)' },
      {
        at_ms: NOW - 5 * HOUR + 5 * 60_000,
        kind: 'count',
        title: '3 of the site’s requests throttled (429)',
      },
    ];
    for (const window of ['24h', '7d']) {
      expect(ticksOf(tallies, window)).toEqual([
        {
          atMs: NOW - 5 * HOUR,
          kind: 'count',
          count: 2,
          titles: tallies.map((t) => t.title),
        },
      ]);
      expect(fullLines(tallies, window)).toEqual([]);
    }
  });
});

describe('the time axis', () => {
  it('names every day at seven days, under the middle of the part the window shows', () => {
    const labels = dayLabels(WEEK_START, NOW, '7d', false);
    expect(labels.map((l) => l.text)).toEqual([
      'Mon 19',
      'Tue 20',
      'Wed 21',
      'Thu 22',
      'Fri 23',
      'Sat 24',
      'Sun 25',
      'Mon 26',
    ]);
    expect(labels[1].atMs).toBe(Date.UTC(2026, 9, 20, 12));
    expect(labels[7].atMs).toBe(Date.UTC(2026, 9, 26, 6));
    expect(dayLabels(WEEK_START, NOW, '7d', true)[1].text).toBe('Tu 20');
  });

  it('keeps Mondays and Fridays at thirty days, where thirty names would crowd', () => {
    const texts = dayLabels(NOW - 30 * DAY, NOW, '30d', false).map((l) => l.text);
    expect(texts.every((t) => t.startsWith('Mon') || t.startsWith('Fri'))).toBe(true);
    expect(texts).toHaveLength(9);
  });

  it('ends each panel in its newest reading', () => {
    expect(nowLabel(planChart().series[0])).toBe('now 6 %');
    expect(nowLabel({ name: 'x', points: [{ at_ms: 0, value: null }] })).toBe('');
  });
});

describe('the hour’s average with its peak', () => {
  it('shades the band between each bucket’s average and its peak, broken where either is missing', () => {
    const x = (at: number) => at;
    const y = (p: number) => 100 - p;
    const band = peakBand(
      [
        { at_ms: 0, value: 10, peak: 40 },
        { at_ms: 1, value: 12, peak: 30 },
        { at_ms: 2, value: null, peak: null },
        { at_ms: 3, value: 5, peak: 9 },
        { at_ms: 4, value: 6, peak: 8 },
      ],
      x,
      y
    );
    expect(band).toBe(
      'M 0.0 60.0 L 1.0 70.0 L 1.0 88.0 L 0.0 90.0 Z M 3.0 91.0 L 4.0 92.0 L 4.0 94.0 L 3.0 95.0 Z'
    );
    expect(peakBand([{ at_ms: 0, value: 1 }], x, y)).toBe('');
  });
});

describe('the legend says only what is not on the chart', () => {
  it('drops the series’ colours, the dashed line, the rolls and Azure’s red mark, and keeps the rest', () => {
    expect(legendRest(planChart().legend)).toEqual(['kept since Tue 20']);
    expect(
      legendRest(
        'teal sent · green written in shadow · red the daily cap of ten · each red mark is a day whose emails drew replies'
      )
    ).toEqual(['red the daily cap of ten', 'each red mark is a day whose emails drew replies']);
  });

  it('keys the rolls, the tallies and what Azure did for the window drawn', () => {
    const chart = planChart([deploy(NOW - DAY, '1.0.4.80')]);
    expect(markKey(chart, '7d')).toEqual([
      'gold: the rolls within an hour of each other, counted; the day view shades each roll',
      'a restart or maintenance, at full height: none in this window',
    ]);
    expect(markKey(chart, '24h')[0]).toBe('gold line: a roll, shaded while it runs');
  });
});

describe('a narrow axis', () => {
  it('joins ticks of one kind closer than a tick is wide, summing their counts', () => {
    const x = (at: number) => at / HOUR;
    const ticks = [
      { atMs: 0, kind: 'deploy' as const, count: 4, titles: ['a'] },
      { atMs: 10 * HOUR, kind: 'deploy' as const, count: 1, titles: ['b'] },
      { atMs: 12 * HOUR, kind: 'count' as const, count: 1, titles: ['c'] },
      { atMs: 40 * HOUR, kind: 'deploy' as const, count: 2, titles: ['d'] },
    ];
    expect(joinClose(ticks, x, 16).map((t) => [t.kind, t.count, t.titles.join('')])).toEqual([
      ['deploy', 5, 'ab'],
      ['count', 1, 'c'],
      ['deploy', 2, 'd'],
    ]);
    // The ticks handed in are not changed.
    expect(ticks[0].count).toBe(4);
  });

  it('drops a day’s name that would run into its neighbour, keeping the newest days', () => {
    const x = (at: number) => at / HOUR;
    const labels = [{ atMs: 0 }, { atMs: 20 * HOUR }, { atMs: 50 * HOUR }, { atMs: 74 * HOUR }];
    expect(spaced(labels, x, 24).map((l) => l.atMs / HOUR)).toEqual([20, 50, 74]);
  });
});

describe('an app’s settings written', () => {
  it('is a counted tick at every window, never a full-height red line', () => {
    const writes: ChartMark[] = [
      {
        at_ms: NOW - 3 * HOUR,
        kind: 'change',
        title: 'UpdateWebSite on APP-THEYARD-ONE-SS, by Azure',
      },
      {
        at_ms: NOW - 3 * HOUR + 60_000,
        kind: 'change',
        title: 'UpdateWebSite on APP-THEYARD-RENDER-SS, by Azure',
      },
    ];
    for (const window of ['24h', '7d', '30d']) {
      expect(ticksOf(writes, window).map((t) => [t.kind, t.count])).toEqual([['change', 2]]);
      expect(fullLines(writes, window)).toEqual([]);
    }
    expect(markKey(planChart(writes), '7d')).toContain(
      'grey tick: an app’s settings written, counted, hover for each'
    );
  });
});

describe('the window a chart spans', () => {
  it('is read from the chart’s own start and end, so a thirty-day chart is drawn as thirty days', () => {
    expect(windowOf(NOW - 30 * DAY, NOW)).toBe('30d');
    expect(windowOf(WEEK_START, NOW)).toBe('7d');
    expect(windowOf(NOW - DAY, NOW)).toBe('24h');
    expect(windowOf(NOW - HOUR, NOW)).toBe('24h');
  });
});
