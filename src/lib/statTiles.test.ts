import { describe, expect, it } from 'vitest';
import {
  afterColdStart,
  COLD_START_MINUTES,
  hourTiming,
  QUIET_BELOW_REQUESTS,
  RINGED_TILES,
  sparkCaption,
  sparkRuns,
  tilesFrom,
  type TileReadings,
  ringOf,
  ringStroke,
  uptimeWords,
  visitorsOn,
} from './statTiles';

const nothing: TileReadings = {
  health: null,
  pages: null,
  traffic: null,
  memory: null,
  charged: null,
  errors: null,
  visitorsToday: null,
};

const quietDay: TileReadings = {
  health: {
    status: 'healthy',
    uptime_seconds: 3 * 3_600 + 12 * 60,
    version: '1.0.0.161',
    commit: 'abc1234',
    checks: [{ status: 'pass' }, { status: 'pass' }, { status: 'pass' }],
  },
  pages: { checked: 116, up: 116 },
  traffic: {
    requests: 240,
    server_errors: 0,
    client_errors: 2,
    warm_requests: 240,
    p50_ms: 12,
    p95_ms: 180,
  },
  memory: { working_set_mb: 310.4, limit_mb: 1185.6 },
  charged: { request_units: 24.49, free_per_second: 1000 },
  errors: 0,
  visitorsToday: 7,
};

const tile = (readings: TileReadings, key: string) => {
  const found = tilesFrom(readings).find((candidate) => candidate.key === key);
  if (found === undefined) throw new Error(`no ${key} tile`);
  return found;
};

describe('the stat tiles', () => {
  it('says a reading has not arrived rather than showing a zero', () => {
    const tiles = tilesFrom(nothing);

    expect(tiles.map((each) => each.key)).toEqual([
      'version',
      'health',
      'pages',
      'speed',
      'memory',
      'charged',
      'errors',
      'visitors',
    ]);
    expect(tiles.every((each) => each.tone === 'waiting')).toBe(true);
    expect(tiles.every((each) => each.value === '…')).toBe(true);
  });

  it('reads a quiet day as good news, in the order the questions are asked', () => {
    const tiles = tilesFrom(quietDay);

    expect(tiles.map((each) => each.question)).toEqual([
      'up',
      'up',
      'up',
      'fast',
      'cost',
      'cost',
      'broke',
      'fast',
    ]);
    expect(tile(quietDay, 'version')).toMatchObject({
      value: '1.0.0.161',
      detail: 'abc1234, up 3h 12m',
    });
    expect(tile(quietDay, 'health')).toMatchObject({
      value: 'Healthy',
      detail: '3 of 3 checks pass',
      tone: 'good',
    });
    expect(tile(quietDay, 'pages')).toMatchObject({ value: '116 of 116', tone: 'good' });
    expect(tile(quietDay, 'speed')).toMatchObject({
      label: 'Typical answer',
      value: '12 ms',
      detail: '95th 180 ms over 240 requests in the last hour',
      tone: 'good',
    });
    expect(tile(quietDay, 'memory')).toMatchObject({
      value: '26%',
      detail: '310 of 1186 MB',
      tone: 'good',
    });
    expect(tile(quietDay, 'charged')).toMatchObject({ value: '24.5', tone: 'plain' });
    expect(tile(quietDay, 'errors')).toMatchObject({ value: '0', tone: 'good' });
    expect(tile(quietDay, 'visitors')).toMatchObject({ value: '7' });
  });

  it('turns amber for worth a look and red for somebody should be looking', () => {
    expect(tile({ ...quietDay, pages: { checked: 116, up: 115 } }, 'pages')).toMatchObject({
      detail: '1 down',
      tone: 'bad',
    });
    expect(
      tile({ ...quietDay, traffic: { ...quietDay.traffic!, p95_ms: 1_400 } }, 'speed').tone
    ).toBe('warn');
    expect(
      tile({ ...quietDay, traffic: { ...quietDay.traffic!, p95_ms: 4_100 } }, 'speed').tone
    ).toBe('bad');
    expect(
      tile({ ...quietDay, memory: { working_set_mb: 1000, limit_mb: 1185.6 } }, 'memory').tone
    ).toBe('warn');
    expect(
      tile({ ...quietDay, memory: { working_set_mb: 1150, limit_mb: 1185.6 } }, 'memory').tone
    ).toBe('bad');
    expect(
      tile(
        { ...quietDay, traffic: { ...quietDay.traffic!, server_errors: 3 }, errors: 1 },
        'errors'
      )
    ).toMatchObject({
      value: '3',
      detail: '3 answered 5xx in the last hour, 1 reported',
      tone: 'bad',
    });
    // A check that fails while the site still calls itself healthy is the fallback serving: amber.
    expect(
      tile(
        {
          ...quietDay,
          health: { ...quietDay.health!, checks: [{ status: 'pass' }, { status: 'fail' }] },
        },
        'health'
      )
    ).toMatchObject({ detail: '1 of 2 checks pass', tone: 'warn' });
  });

  it('says a quiet hour is quiet, and that is not a speed', () => {
    expect(
      tile(
        {
          ...quietDay,
          traffic: {
            requests: 0,
            server_errors: 0,
            client_errors: 0,
            warm_requests: 0,
            p50_ms: null,
            p95_ms: null,
          },
        },
        'speed'
      )
    ).toMatchObject({ value: 'quiet', tone: 'plain' });
  });

  it('writes an uptime in its two largest units', () => {
    expect(uptimeWords(59)).toBe('0m');
    expect(uptimeWords(3_600 + 120)).toBe('1h 2m');
    expect(uptimeWords(2 * 86_400 + 5 * 3_600 + 60)).toBe('2d 5h');
  });

  it('draws the hour under a tile as runs, and leaves a gap where nothing was measured', () => {
    expect(sparkRuns([0, 10, null, 5, 5], 100, 24)).toEqual(['0,23 25,1', '75,12 100,12']);
    // One reading is not a line, and neither is none.
    expect(sparkRuns([null, 4, null], 100, 24)).toEqual([]);
    expect(sparkRuns([], 100, 24)).toEqual([]);
    // A flat zero is drawn along the floor and not divided by.
    expect(sparkRuns([0, 0, 0], 100, 24)).toEqual(['0,23 50,23 100,23']);
  });

  it('hands the hour to the tile it belongs to', () => {
    const tiles = tilesFrom({ ...quietDay, sparks: { speed: [1, 2], memory: [3, 4] } });
    expect(tiles.find((t) => t.key === 'speed')?.spark).toEqual([1, 2]);
    expect(tiles.find((t) => t.key === 'memory')?.spark).toEqual([3, 4]);
    expect(tiles.find((t) => t.key === 'version')?.spark).toBeUndefined();
  });

  it('says there is no document store where there is none, and does not wait for one', () => {
    expect(tile({ ...quietDay, charged: 'none' }, 'charged')).toMatchObject({
      value: 'none',
      tone: 'plain',
    });
  });

  it('counts today and only today', () => {
    const days = [
      { day: '2026-09-19', humans: 7 },
      { day: '2026-09-20', humans: 3 },
    ];
    expect(visitorsOn(days, new Date('2026-09-20T23:59:00Z'))).toBe(3);
    expect(visitorsOn(days, new Date('2026-09-21T00:01:00Z'))).toBe(0);
  });

  it('counts the people, not every token that was not a bot, once the report says who', () => {
    const days = [{ day: '2026-09-23', humans: 757, people: 90 }];
    expect(visitorsOn(days, new Date('2026-09-23T12:00:00Z'))).toBe(90);
  });

  it('says when the slowest minute was, so the tile is somewhere to start', () => {
    expect(
      tile(
        {
          ...quietDay,
          traffic: {
            requests: 74,
            server_errors: 0,
            client_errors: 0,
            warm_requests: 74,
            p50_ms: 8,
            p95_ms: 1212,
            slowest_label: '07:32',
          },
        },
        'speed'
      )
    ).toMatchObject({
      value: '8 ms',
      detail: '95th 1212 ms over 74 requests in the last hour, slowest at 07:32',
      tone: 'warn',
    });
  });

  it('says what the lines are lines of, and that the number is still now', () => {
    expect(sparkCaption('last hour', 'hour')).toBe('The line under a tile is the last hour.');
    expect(sparkCaption('last 7 days', 'reading')).toContain('still the last hour');
    expect(sparkCaption('last 7 days', 'not-kept')).toBe(
      'The last 7 days is not kept here, so the lines are still the last hour.'
    );
    expect(sparkCaption('last 30 days', 'kept')).toContain('The number over it is still now.');
  });

  it('does not hold the minutes of a cold start against the hour, and says it left them out', () => {
    const slots = ['12:30', '12:31', '12:32', '12:33', '12:40'].map((time) => ({
      at: `2026-09-20T${time}:00Z`,
    }));
    const { warm, left_out } = afterColdStart(slots, new Date('2026-09-20T12:30:40Z'));
    expect(left_out.map((slot) => slot.at.slice(11, 16))).toEqual(['12:30', '12:31', '12:32']);
    expect(warm.map((slot) => slot.at.slice(11, 16))).toEqual(['12:33', '12:40']);
    expect(COLD_START_MINUTES).toBe(3);
    // A start nobody knows the time of leaves nothing out.
    expect(afterColdStart(slots, null).warm).toHaveLength(5);
    expect(
      tile(
        {
          ...quietDay,
          traffic: {
            requests: 74,
            server_errors: 0,
            client_errors: 0,
            warm_requests: 60,
            p50_ms: 5,
            p95_ms: 320,
            slowest_label: '07:41',
            cold_start_label: '07:30',
          },
        },
        'speed'
      )
    ).toMatchObject({
      value: '5 ms',
      detail:
        '95th 320 ms over 60 requests in the last hour, slowest at 07:41; the start at 07:30 is left out',
      tone: 'good',
    });
  });

  it("reads the hour's own median and ninety-fifth over every request in it, nearest rank", () => {
    // Three minutes: the 6355 ms that started the question (2026-09-22) sits
    // in one of them, and the hour's ninety-fifth over 40 requests is not it.
    const slots = [
      { at: '2026-09-22T11:22:00Z', requests: 3, durations_ms: [1, 2, 3] },
      { at: '2026-09-22T11:23:00Z', requests: 8, durations_ms: [4, 5, 6, 7, 8, 9, 10, 6355] },
      { at: '2026-09-22T11:24:00Z', requests: 0, durations_ms: [] },
      {
        at: '2026-09-22T11:30:00Z',
        requests: 29,
        durations_ms: Array.from({ length: 29 }, (_, i) => 20 + i),
      },
    ];
    expect(hourTiming(slots)).toEqual({
      requests: 40,
      p50_ms: 29,
      p95_ms: 47,
      slowest_at: '2026-09-22T11:23:00Z',
    });
    // One minute of one request: its median, its ninety-fifth and its slowest are that request.
    expect(hourTiming([{ at: 'a', durations_ms: [6355] }])).toEqual({
      requests: 1,
      p50_ms: 6355,
      p95_ms: 6355,
      slowest_at: 'a',
    });
    expect(hourTiming([])).toEqual({ requests: 0, p50_ms: null, p95_ms: null, slowest_at: null });
    // A kept slot carries no durations and counts for nothing here.
    expect(hourTiming([{ at: 'a', requests: 12 }]).requests).toBe(0);
  });

  it('says under a millisecond rather than zero, because the ring keeps whole milliseconds', () => {
    expect(
      tile({ ...quietDay, traffic: { ...quietDay.traffic!, p50_ms: 0, p95_ms: 62 } }, 'speed')
    ).toMatchObject({ value: 'under 1 ms', tone: 'good' });
  });

  it('reads quiet under twenty requests, with the numbers, and colours only a busy hour', () => {
    expect(QUIET_BELOW_REQUESTS).toBe(20);
    // Ten requests, one of them 6355 ms: too few to colour, and the tile says so and shows both numbers.
    const quiet = tile(
      {
        ...quietDay,
        traffic: {
          ...quietDay.traffic!,
          warm_requests: 10,
          p50_ms: 4,
          p95_ms: 6355,
          slowest_label: '06:20',
        },
      },
      'speed'
    );
    expect(quiet).toMatchObject({
      value: 'quiet',
      tone: 'plain',
      detail:
        '10 requests in the last hour, too few to judge; typical 4 ms, 95th 6355 ms, slowest at 06:20',
    });
    // The same ninety-fifth over forty requests is somebody should be looking.
    expect(
      tile(
        { ...quietDay, traffic: { ...quietDay.traffic!, warm_requests: 40, p95_ms: 5998 } },
        'speed'
      ).tone
    ).toBe('bad');
    // Exactly the floor is enough for a colour.
    expect(
      tile(
        { ...quietDay, traffic: { ...quietDay.traffic!, warm_requests: 20, p95_ms: 1254 } },
        'speed'
      ).tone
    ).toBe('warn');
    expect(
      tile(
        { ...quietDay, traffic: { ...quietDay.traffic!, warm_requests: 19, p95_ms: 1254 } },
        'speed'
      ).tone
    ).toBe('plain');
  });

  it('calls an hour whose only requests were the cold start warming, not quiet', () => {
    const traffic = {
      requests: 67,
      server_errors: 0,
      client_errors: 0,
      warm_requests: 0,
      p50_ms: null,
      p95_ms: null,
    };
    expect(
      tile({ ...quietDay, traffic: { ...traffic, cold_start_label: '10:20' } }, 'speed')
    ).toMatchObject({
      value: 'warming',
      tone: 'plain',
    });
    expect(tile({ ...quietDay, traffic: { ...traffic, requests: 0 } }, 'speed').value).toBe(
      'quiet'
    );
  });
});

describe('the ring beside a number', () => {
  it('is drawn only on the tiles that hold its room (1.0.3.24)', () => {
    const full = { ...quietDay, pages: { checked: 1000, up: 999 } };
    for (const tile of tilesFrom(full)) {
      if (tile.ring !== undefined) expect(RINGED_TILES).toContain(tile.key);
    }
  });

  it('is a share of a known whole, and only the three tiles that have one carry it', () => {
    const tiles = tilesFrom(quietDay);
    expect(tiles.filter((each) => each.ring !== undefined).map((each) => each.key)).toEqual([
      'health',
      'pages',
      'memory',
    ]);
    expect(tiles.find((each) => each.key === 'health')?.ring).toEqual({ share: 1, label: '3/3' });
    expect(tiles.find((each) => each.key === 'pages')?.ring).toEqual({ share: 1, label: '100%' });
  });

  it('never rounds a page that is down up to a full ring', () => {
    const tiles = tilesFrom({ ...quietDay, pages: { checked: 1000, up: 999 } });
    expect(tiles.find((each) => each.key === 'pages')?.ring?.label).toBe('99%');
  });

  it('draws nothing where there is no whole, and clamps a share that overruns it', () => {
    expect(ringOf(3, 0, '3/0')).toBeUndefined();
    expect(ringOf(12, 10, '120%')).toEqual({ share: 1, label: '120%' });
    expect(ringOf(-1, 10, '0')).toEqual({ share: 0, label: '0' });
  });

  it('turns a share into a stroke: the whole circle, and the part of it left undrawn', () => {
    expect(ringStroke(1, 10)).toEqual({ length: 62.8, gap: 0 });
    expect(ringStroke(0.5, 10)).toEqual({ length: 62.8, gap: 31.4 });
    expect(ringStroke(0, 10)).toEqual({ length: 62.8, gap: 62.8 });
  });
});
