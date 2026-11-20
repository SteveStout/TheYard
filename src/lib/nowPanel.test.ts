import { describe, expect, it } from 'vitest';
import {
  agoWords,
  asOfLine,
  NO_READING,
  nextReading,
  rightNowRows,
  thisWeekRows,
} from './nowPanel';
import type { Pulse } from './pulse';

const NOW = Date.UTC(2026, 10, 20, 15, 0, 0);

const pulse = (overrides: Partial<Pulse> = {}): Pulse => ({
  usual_p95_ms_24h: 409,
  requests_24h: 21_312,
  month_to_date: 43.04,
  forecast_month: 77.1,
  currency: 'USD',
  cost_read_at_ms: NOW - 60_000,
  uptime_seconds: 480,
  version: '1.0.5.128',
  commit: 'a4c45c4',
  read_at_ms: NOW,
  people_all_time: 1274,
  people_new_by_day: [],
  counted_from: '2026-10-24',
  visits_all_time: null,
  visits_from: null,
  visits_by_day: [],
  ...overrides,
});

describe("the dark live panel's rows (ADR: The sales pages set the site's look)", () => {
  it('fresh: the first screen’s three readings and TheYard’s three, each with its line', () => {
    expect(rightNowRows(pulse(), NOW)).toEqual([
      { key: 'p95', label: 'Answering in', value: '409 ms', under: 'p95, last 24 hours' },
      { key: 'release', label: 'Last release', value: '8 min ago', under: 'version 1.0.5.128' },
      { key: 'bill', label: 'Azure bill', value: '$43.04', under: 'this month so far' },
    ]);
    expect(thisWeekRows(pulse(), 78)).toEqual([
      { key: 'requests', label: 'Requests answered', value: '21,312', under: 'last 24 hours' },
      { key: 'releases', label: 'Releases', value: '78', under: 'last 7 days, one gate each' },
      { key: 'people', label: 'People who visited', value: '1,274', under: 'since 24 Oct' },
    ]);
    // The release's age runs on the page's clock from the instant the server rolled, not from the read.
    expect(rightNowRows(pulse(), NOW + 2 * 3_600_000)[1].value).toBe('2 h 8 min ago');
  });

  it('never read: a figure the server has not read hides its row, never a dash, NaN or 0 ms; nothing read, no rows', () => {
    expect(rightNowRows(null, NOW)).toEqual([]);
    expect(thisWeekRows(null, 0)).toEqual([]);
    expect(thisWeekRows(null, 5).map((row) => row.key)).toEqual(['releases']);
    const unread = pulse({
      usual_p95_ms_24h: null,
      month_to_date: null,
      requests_24h: 0,
      people_all_time: null,
    });
    expect(rightNowRows(unread, NOW).map((row) => row.key)).toEqual(['release']);
    expect(rightNowRows(pulse({ usual_p95_ms_24h: 0 }), NOW).map((row) => row.key)).toEqual([
      'release',
      'bill',
    ]);
    expect(rightNowRows(pulse({ uptime_seconds: Number.NaN }), NOW).map((row) => row.key)).toEqual([
      'p95',
      'bill',
    ]);
    expect(thisWeekRows(unread, 78).map((row) => row.key)).toEqual(['releases']);
    expect(thisWeekRows(pulse({ counted_from: null }), 1)[2].under).toBe('all time');
    expect(rightNowRows(pulse({ version: 'dev' }), NOW)[1].under).toBe('dev build');
    for (const row of [...rightNowRows(pulse(), NOW), ...thisWeekRows(pulse(), 78)]) {
      expect(row.value).not.toMatch(/^-+$|NaN|undefined|null|^0 ms$|^$/);
    }
  });

  it('says how long ago in minutes, then hours and minutes, then days and hours, never under a minute', () => {
    expect(agoWords(0)).toBe('1 min ago');
    expect(agoWords(-5_000)).toBe('1 min ago');
    expect(agoWords(59 * 60_000)).toBe('59 min ago');
    expect(agoWords(165 * 60_000)).toBe('2 h 45 min ago');
    expect(agoWords((3 * 24 + 4) * 3_600_000)).toBe('3 d 4 h ago');
  });
});

describe('usePulse’s reading: one read after another (nextReading)', () => {
  it('a good answer replaces the last and is fresh, with no "as of"', () => {
    const read = nextReading(NO_READING, pulse());
    expect(read).toEqual({ pulse: pulse(), failed: false });
    expect(asOfLine(read)).toBeNull();
  });

  it('stale: a failed read keeps the last value and says "as of HH:MM", the last answer’s own time', () => {
    const fresh = nextReading(NO_READING, pulse());
    const stale = nextReading(fresh, null);
    expect(stale.pulse).toEqual(pulse());
    expect(stale.failed).toBe(true);
    expect(asOfLine(stale, 'UTC')).toBe('as of 15:00');
    expect(asOfLine(stale, 'America/Chicago')).toBe('as of 09:00');
    // The panel still draws the last values under its "as of".
    expect(rightNowRows(stale.pulse, NOW)).toHaveLength(3);
  });

  it('never read: a failed first read has nothing to keep and no "as of"; a wrong-shaped answer is no reading', () => {
    const never = nextReading(NO_READING, null);
    expect(never).toEqual({ pulse: null, failed: true });
    expect(asOfLine(never)).toBeNull();
    expect(nextReading(nextReading(NO_READING, pulse()), { nonsense: true })).toEqual({
      pulse: null,
      failed: false,
    });
    expect(asOfLine({ pulse: pulse({ read_at_ms: Number.NaN }), failed: true })).toBeNull();
  });
});
