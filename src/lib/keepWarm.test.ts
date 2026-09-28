import { describe, expect, it } from 'vitest';
import { keptWarmLine } from './keepWarm';

describe('the kept-warm line', () => {
  it('says when the last pass ran, how many reads it sent and the slowest', () => {
    const line = keptWarmLine({
      last_pass: '2026-09-28T17:04:00Z',
      reads: 48,
      failed: 0,
      slowest_ms: 1312,
      slowest: '/api/admin/activity?window=30d (cosmos)',
    });
    expect(line).toMatch(/^Kept warm: last pass \d\d:\d\d, 48 reads, slowest 1,312 ms$/);
  });

  it('names the reads that did not answer', () => {
    const line = keptWarmLine({
      last_pass: '2026-09-28T17:04:00Z',
      reads: 48,
      failed: 2,
      slowest_ms: 90,
      slowest: null,
    });
    expect(line).toContain(', 2 did not answer');
  });

  it('says so when the loop is off, or has not run yet', () => {
    expect(keptWarmLine(null)).toBe('Kept warm: off on this container');
    expect(keptWarmLine(undefined)).toBe('Kept warm: off on this container');
    expect(
      keptWarmLine({ last_pass: null, reads: 0, failed: 0, slowest_ms: 0, slowest: null })
    ).toBe('Kept warm: the first pass is on its way');
  });
});
