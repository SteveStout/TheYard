import { describe, expect, it } from 'vitest';
import { keepPolling, liveWords, type LiveReport, newest, pollMs } from './live';

const NOW = Date.parse('2026-11-07T22:00:00Z');

const report = (overrides: Partial<LiveReport> = {}): LiveReport => ({
  available: true,
  note: null,
  grain_seconds: 15,
  source: "the database's own view",
  as_of_ms: NOW,
  samples: [
    { at: new Date(NOW - 20_000).toISOString(), values: { dtu: 12.34, cpu: 3 } },
    { at: new Date(NOW - 5_000).toISOString(), values: { dtu: 14.06, cpu: null } },
  ],
  ...overrides,
});

describe('a card live line (ADR: Live dials where the source allows)', () => {
  it('asks at the source grain, never faster than every two seconds', () => {
    expect(pollMs(report())).toBe(15_000);
    expect(pollMs(report({ grain_seconds: 1 }))).toBe(2_000);
    expect(pollMs(null)).toBe(2_000);
  });

  it('keeps asking while open, in view and live, and stops for good on a card with no source', () => {
    expect(keepPolling(true, 'visible', null)).toBe(true);
    expect(keepPolling(true, 'visible', report())).toBe(true);
    expect(keepPolling(true, 'hidden', report())).toBe(false);
    expect(keepPolling(false, 'visible', report())).toBe(false);
    expect(keepPolling(true, 'visible', report({ available: false }))).toBe(false);
  });

  it('says the source, the grain and the newest reading with its age', () => {
    expect(newest(report())?.values.dtu).toBe(14.06);
    expect(liveWords(report(), NOW)).toBe(
      "live: the database's own view: dtu 14.1 · cpu not read, 5 s ago"
    );
    expect(liveWords(report({ source: null }), NOW)).toBe(
      'live: every 15 s: dtu 14.1 · cpu not read, 5 s ago'
    );
  });

  it('says why there is no reading, and what it waits for', () => {
    expect(liveWords(null, NOW)).toBe('live: not read yet');
    expect(liveWords(report({ available: false, note: 'this card has no live source' }), NOW)).toBe(
      'this card has no live source'
    );
    expect(liveWords(report({ available: false, note: null }), NOW)).toBe('no live source');
    expect(liveWords(report({ samples: [] }), NOW)).toBe(
      "live: the database's own view: the first sample is on its way"
    );
    expect(newest(report({ samples: [] }))).toBeNull();
  });
});
