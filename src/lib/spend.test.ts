import { describe, expect, it } from 'vitest';
import {
  costWindowFor,
  type CostReport,
  dayShort,
  dayWords,
  donutArcs,
  headline,
  money,
  niceCeiling,
  spendLine,
  typeBars,
} from './spend';

/** A month of the real shape, small enough to read: two reported days, the newest partial, one forecast day. */
const report = (over: Partial<CostReport> = {}): CostReport => ({
  window: '30d',
  windows: ['24h', '7d', '30d'],
  available: true,
  note: null,
  currency: 'USD',
  read_at_ms: 1_790_000_000_000,
  newest_day: '2026-09-29',
  newest_partial: true,
  month: '2026-09',
  month_to_date: 47.18,
  forecast_month: 49.03,
  window_total: 2,
  days: [
    { day: '2026-09-28', cost: 1, total: 1, partial: false },
    { day: '2026-09-29', cost: 1, total: 2, partial: true },
  ],
  forecast: [{ day: '2026-09-30', cost: 1.5, total: 3.5, partial: false }],
  resources: [
    {
      name: 'aci-theyard-ss',
      type: 'microsoft.containerinstance/containergroups',
      cost: 1.5,
      share: 75,
      count: 1,
    },
    { name: 'Others', type: 'others', cost: 0.5, share: 25, count: 3 },
  ],
  types: [
    { type: 'microsoft.web/sites', label: 'App Service apps', resources: 3, cost: 0 },
    { type: 'microsoft.sql/servers/databases', label: 'SQL databases', resources: 1, cost: 0.5 },
  ],
  ...over,
});

describe('what Azure charges (ADR: What Azure charges)', () => {
  it('shows the hour every other chart offers as the last day, because Azure bills by the day', () => {
    expect(costWindowFor('1h')).toBe('24h');
    expect(costWindowFor('24h')).toBe('24h');
    expect(costWindowFor('7d')).toBe('7d');
    expect(costWindowFor('30d')).toBe('30d');
  });

  it('writes money as the bill does, and a charge under a cent as that', () => {
    expect(money(47.18)).toBe('$47.18');
    expect(money(0)).toBe('$0.00');
    expect(money(0.001)).toBe('under $0.01');
    expect(money(1234.5)).toBe('$1,234.50');
  });

  it('names a day the way a person reads it', () => {
    expect(dayWords('2026-09-30')).toBe('30 September');
    expect(dayShort('2026-09-30')).toBe('30 Sep');
  });

  it('says the month so far, the forecast, and which day Azure is still adding to', () => {
    expect(headline(report())).toEqual([
      'September so far: $47.18.',
      'Azure forecasts $49.03 by the end of September.',
      '29 September is still being added to: Azure reports a day eight to twenty four hours late.',
    ]);
    expect(headline(report({ forecast_month: null, newest_partial: false }))[1]).toBe(
      'Azure has not forecast the rest of the month yet.'
    );
    expect(headline(report({ available: false, month: null }))).toEqual([]);
  });

  it('rounds the axis up to 1, 2 or 5 times a power of ten', () => {
    expect(niceCeiling(3.5)).toBe(5);
    expect(niceCeiling(47.18)).toBe(50);
    expect(niceCeiling(0)).toBe(1);
    expect(niceCeiling(120)).toBe(200);
  });

  it('draws the running total, and turns to the forecast where the reported days end', () => {
    const line = spendLine(report(), true);
    expect(line.ceiling).toBe(5);
    expect(line.actual.split(' ').length).toBe(2);
    // The forecast starts on the newest reported point, so the two read as one line.
    expect(line.forecast.startsWith(line.actual.split(' ')[1].replace('L', 'M'))).toBe(true);
    expect(line.newest?.partial).toBe(true);
    expect(line.ends.map((end) => end.label)).toEqual(['28 Sep', '30 Sep']);
    // Without the forecast the line ends on the newest day.
    expect(spendLine(report(), false).forecast).toBe('');
    expect(spendLine(report(), false).ends.map((end) => end.label)).toEqual(['28 Sep', '29 Sep']);
  });

  it('cuts the donut into shares of the whole, with Others in the neutral tone', () => {
    const arcs = donutArcs(report().resources);
    expect(arcs.map((arc) => arc.tone)).toEqual([1, 0]);
    expect(arcs.every((arc) => arc.path.startsWith('M') && arc.path.endsWith('Z'))).toBe(true);
    // A single slice is the whole ring, drawn as two halves.
    expect(
      donutArcs([{ name: 'one', type: 't', cost: 3, share: 100, count: 1 }])[0].path.split('Z')
        .length
    ).toBe(3);
    expect(donutArcs([])).toEqual([]);
  });

  it('draws each type as a share of the type with the most resources', () => {
    expect(typeBars(report().types).map((bar) => bar.share)).toEqual([1, 1 / 3]);
    expect(typeBars([])).toEqual([]);
  });
});
