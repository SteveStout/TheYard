import { describe, expect, it } from 'vitest';
import {
  barHeights,
  buildWords,
  forecastWords,
  dayWords,
  LATENCY_CEILING_MS,
  moneyWords,
  pulseFrom,
  unitWords,
  share,
  ticks,
  UNREAD,
  uptimeWords,
} from './pulse';

const answer = {
  usual_p95_ms_24h: 31,
  requests_24h: 1840,
  month_to_date: 16.95,
  forecast_month: 33.39,
  currency: 'USD',
  cost_read_at_ms: 1_760_700_000_000,
  uptime_seconds: 27_180,
  version: '1.0.3.156',
  commit: '3b24c42',
  read_at_ms: 1_760_700_100_000,
  people_all_time: 1284,
  people_new_by_day: [3, 0, 7],
  counted_from: '2026-10-24',
  visits_all_time: 2104,
  visits_from: '2026-09-02',
  visits_by_day: [61, 70, 58],
};

describe('an answer from /api/pulse', () => {
  it('is read into the shape, with every figure as it came', () => {
    expect(pulseFrom(answer)).toEqual(answer);
  });

  it('reads a null figure as unread rather than refusing the answer', () => {
    const unread = { ...answer, usual_p95_ms_24h: null, month_to_date: null, forecast_month: null };
    expect(pulseFrom(unread)?.usual_p95_ms_24h).toBeNull();
    expect(pulseFrom(unread)?.month_to_date).toBeNull();
  });

  it('reads an answer from before the people count as a count not yet begun, not as the wrong shape', () => {
    const {
      people_all_time: _people,
      people_new_by_day: _days,
      counted_from: _from,
      ...older
    } = answer;
    const read = pulseFrom(older);
    expect(read?.version).toBe('1.0.3.156');
    expect(read?.people_all_time).toBeNull();
    expect(read?.people_new_by_day).toEqual([]);
    expect(read?.counted_from).toBeNull();
  });

  it('reads the kept visits only with the day they were kept from, and an older answer as none', () => {
    const { visits_all_time: _visits, visits_from: _since, ...older } = answer;
    expect(pulseFrom(older)?.visits_all_time).toBeNull();
    expect(pulseFrom(older)?.visits_from).toBeNull();
    expect(pulseFrom({ ...answer, visits_from: 'Sep 2' })?.visits_all_time).toBeNull();
    expect(pulseFrom({ ...answer, visits_all_time: null })?.visits_from).toBeNull();
    expect(pulseFrom({ ...answer, visits_all_time: null })?.visits_by_day).toEqual([]);
    expect(pulseFrom({ ...answer, visits_by_day: ['a'] })?.visits_by_day).toEqual([]);
  });

  it('says a unit in the right number at none, one and many', () => {
    expect(unitWords(0, 'person', 'people')).toBe('0 people');
    expect(unitWords(1, 'person', 'people')).toBe('1 person');
    expect(unitWords(1, 'visit', 'visits')).toBe('1 visit');
    expect(unitWords(2, 'visit', 'visits')).toBe('2 visits');
    expect(unitWords(2104, 'visit', 'visits')).toBe('2,104 visits');
  });

  it('keeps a people count of null as not counted yet, and drops a day list that is not numbers', () => {
    const notYet = pulseFrom({ ...answer, people_all_time: null, people_new_by_day: ['a'] });
    expect(notYet?.people_all_time).toBeNull();
    expect(notYet?.people_new_by_day).toEqual([]);
    expect(pulseFrom({ ...answer, counted_from: 'Oct 24' })?.counted_from).toBeNull();
  });

  it('is null when it is not that shape, so the band says so instead of drawing a figure it does not have', () => {
    expect(pulseFrom(null)).toBeNull();
    expect(pulseFrom({ ...answer, uptime_seconds: 'a while' })).toBeNull();
    expect(pulseFrom({ ...answer, currency: 7 })).toBeNull();
    const { version: _version, ...noVersion } = answer;
    expect(pulseFrom(noVersion)).toBeNull();
  });
});

describe('the words under each instrument', () => {
  it('says the day a count began as the day and the month', () => {
    expect(dayWords('2026-10-24')).toBe('24 Oct');
    expect(dayWords('2026-09-13')).toBe('13 Sep');
    expect(dayWords(null)).toBe(UNREAD);
  });

  it('draws the thirty days against the tallest of them, and a run of zeros as empty bars', () => {
    expect(barHeights([2, 4, 0])).toEqual([0.5, 1, 0]);
    expect(barHeights([0, 0])).toEqual([0, 0]);
    expect(barHeights([])).toEqual([]);
  });

  it('says how long since the roll in the two units that carry it', () => {
    expect(uptimeWords(27_180)).toBe('7 h 33 m');
    expect(uptimeWords(2_460)).toBe('41 m');
    expect(uptimeWords(273_600)).toBe('3 d 4 h');
    expect(uptimeWords(-1)).toBe(UNREAD);
  });

  it('writes a shipped build with its commit, and a local one in words', () => {
    expect(buildWords('1.0.3.159', '0149834')).toBe('1.0.3.159 at 0149834');
    expect(buildWords('dev', 'local')).toBe('dev build');
  });

  it('prints money with its symbol, and the forecast as whole currency', () => {
    expect(moneyWords(16.95, 'USD')).toBe('$16.95');
    expect(moneyWords(33.39, 'USD', true)).toBe('$33');
    expect(moneyWords(1234.5, 'EUR')).toBe('€1,234.50');
    expect(moneyWords(12, 'SEK')).toBe('12.00 SEK');
    expect(moneyWords(null, 'USD')).toBe(UNREAD);
  });
});

describe('the bill instrument (the cost lane, 24 Oct addendum)', () => {
  it('reads the forecast in whole currency, at least the spend when a forecast is below it, and nothing unread', () => {
    expect(forecastWords(70.2, 84.94, 'USD')).toBe('forecast $85');
    expect(forecastWords(70.2, 60.58, 'USD')).toBe('at least $70.20');
    expect(forecastWords(0, 0, 'USD')).toBe('forecast $0');
    expect(forecastWords(1, 1, 'USD')).toBe('forecast $1');
    expect(forecastWords(70.2, null, 'USD')).toBe('');
    expect(forecastWords(null, 84.94, 'USD')).toBe('');
  });
});

describe('the scale', () => {
  it('puts the marker where the reading is, and pins it at the end past the ceiling', () => {
    expect(share(31, LATENCY_CEILING_MS)).toBeCloseTo(0.31);
    expect(share(250, LATENCY_CEILING_MS)).toBe(1);
    expect(share(null, LATENCY_CEILING_MS)).toBe(0);
    expect(share(-4, LATENCY_CEILING_MS)).toBe(0);
  });

  it('graduates every five with a number every twenty-five', () => {
    const marks = ticks(LATENCY_CEILING_MS);
    expect(marks).toHaveLength(21);
    expect(marks.filter((mark) => mark.major).map((mark) => mark.at)).toEqual([0, 25, 50, 75, 100]);
  });
});
