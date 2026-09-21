import { describe, expect, it } from 'vitest';
import type { TrafficSlot } from './machineChart';
import { SLOW_P95_MS } from './statTiles';
import { TRAFFIC_CHARTS, failSentence, trafficBlocks, typicalMs } from './trafficCard';

const slot = (p50: number | null, p95: number | null, requests = 1): TrafficSlot => ({
  at: '2026-09-21T12:00:00Z',
  requests: p50 === null ? 0 : requests,
  p50_ms: p50,
  p95_ms: p95,
  server_errors: 0,
  client_errors: 0,
});

const quiet = { requests: 33, server_errors: 0, client_errors: 7, slowest_p95_ms: 139 };

describe('typicalMs', () => {
  it('is the middle one of the medians the slots hold, by nearest rank', () => {
    expect(typicalMs([slot(30, 90), slot(10, 20), slot(12, 40)])).toBe(12);
    // An even count takes the lower of the two middle ones, never a number no minute read.
    expect(typicalMs([slot(30, 90), slot(10, 20), slot(12, 40), slot(14, 50)])).toBe(12);
  });

  it('leaves out a minute nobody asked anything in, which has no median', () => {
    expect(typicalMs([slot(null, null), slot(8, 9), slot(null, null)])).toBe(8);
    expect(typicalMs([slot(null, null)])).toBeNull();
    expect(typicalMs([])).toBeNull();
  });
});

describe('trafficBlocks', () => {
  it('says the hour in four plain numbers, in the order they are read', () => {
    const blocks = trafficBlocks([slot(12, 139), slot(10, 40)], quiet, 'last hour', true);
    expect(blocks.map((block) => block.key)).toEqual(['requests', 'typical', 'slow', 'errors']);
    expect(blocks[0]).toMatchObject({
      label: 'Requests',
      value: '33',
      detail: 'requests in the last hour: pages and API calls',
      tone: 'plain',
    });
    expect(blocks[1]).toMatchObject({
      label: 'Typical answer',
      value: '10 ms',
      detail: 'half of requests were faster, in the typical minute',
    });
    expect(blocks[2]).toMatchObject({
      label: 'Slow answers',
      value: '139 ms',
      detail: '19 in 20 requests beat this, in the slowest minute',
    });
    expect(blocks[3]).toMatchObject({
      label: 'Server errors',
      value: '0',
      detail: '7 more were turned away (bad address or not signed in)',
      tone: 'good',
      word: 'none',
    });
  });

  it('never writes a status code or a percentile a reader has to know', () => {
    const words = trafficBlocks([slot(12, 139)], quiet, 'last hour', true)
      .map((block) => `${block.label} ${block.detail}`)
      .join(' ');
    expect(words).not.toMatch(/5xx|4xx|ninety-fifth|p95|median/i);
  });

  it('calls an answer fast under the speed tile’s own amber line, and says nothing over it', () => {
    const fast = trafficBlocks([slot(12, 139)], quiet, 'last hour', true);
    expect(fast[1]).toMatchObject({ tone: 'good', word: 'fast' });
    expect(fast[2]).toMatchObject({ tone: 'good', word: 'fast' });
    // The alarm is the tile's, which knows about a cold start; this block has no warning tone to give.
    const slow = trafficBlocks(
      [slot(12, SLOW_P95_MS)],
      { ...quiet, slowest_p95_ms: SLOW_P95_MS },
      'last hour',
      true
    );
    expect(slow[2]).toMatchObject({ tone: 'plain', word: null, value: '1,000 ms' });
  });

  it('turns the errors block red on one server error, and only on a server error', () => {
    const turnedAway = trafficBlocks([], { ...quiet, client_errors: 400 }, 'last hour', true);
    expect(turnedAway[3].tone).toBe('good');
    const failing = trafficBlocks([], { ...quiet, server_errors: 1 }, 'last hour', true);
    expect(failing[3]).toMatchObject({ value: '1', tone: 'bad', word: 'needs attention' });
  });

  it('counts one as one, and says so when nobody was turned away or nobody asked', () => {
    const one = trafficBlocks(
      [],
      { requests: 1, server_errors: 0, client_errors: 1, slowest_p95_ms: null },
      'last 7 days',
      false
    );
    expect(one[0].detail).toBe('request in the last 7 days: pages and API calls');
    expect(one[1]).toMatchObject({
      value: 'none',
      detail: 'nobody asked for anything',
      tone: 'plain',
    });
    expect(one[2]).toMatchObject({ value: 'none', tone: 'plain' });
    expect(one[3].detail).toBe('1 more was turned away (bad address or not signed in)');
    const none = trafficBlocks([], { ...quiet, client_errors: 0 }, 'last hour', true);
    expect(none[3].detail).toBe('none were turned away');
  });

  it('calls a kept window’s slot a stretch, because it is not a minute', () => {
    const blocks = trafficBlocks([slot(12, 139)], quiet, 'last 30 days', false);
    expect(blocks[1].detail).toBe('half of requests were faster, in the typical stretch');
    expect(blocks[2].detail).toBe('19 in 20 requests beat this, in the slowest stretch');
  });
});

describe('failSentence', () => {
  it('says a flat zero in words, so it reads as good news and not as a chart that did not load', () => {
    expect(failSentence(quiet, 'last hour')).toEqual({
      good: true,
      text: 'No server errors in the last hour.',
    });
  });

  it('counts them when there are any', () => {
    expect(failSentence({ ...quiet, server_errors: 1 }, 'last hour')).toEqual({
      good: false,
      text: '1 server error in the last hour.',
    });
    expect(failSentence({ ...quiet, server_errors: 12 }, 'last 24 hours').text).toBe(
      '12 server errors in the last 24 hours.'
    );
  });
});

describe('TRAFFIC_CHARTS', () => {
  it('titles every chart with a question and gives every axis a unit', () => {
    for (const chart of Object.values(TRAFFIC_CHARTS)) {
      expect(chart.title.endsWith('?')).toBe(true);
      expect(chart.read.length).toBeGreaterThan(0);
      expect(chart.unit.length).toBeGreaterThan(0);
    }
    expect(TRAFFIC_CHARTS.errors.title).toBe('Did anything fail?');
  });

  it('puts the plain word first and the term in brackets', () => {
    expect(TRAFFIC_CHARTS.errors.server).toMatch(/^Server errors \(5xx/);
    expect(TRAFFIC_CHARTS.errors.turnedAway).toMatch(/^Turned away \(4xx/);
    expect(TRAFFIC_CHARTS.timing.typical).toBe('Typical request (median)');
    expect(TRAFFIC_CHARTS.timing.slow).toBe('Slow requests (95th percentile)');
  });
});
