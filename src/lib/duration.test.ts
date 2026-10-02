import { describe, expect, it } from 'vitest';
import { durationWords, SECONDS_FROM_MS } from './duration';

/**
 * Durations on the Admin cards: grouped by thousands below ten seconds,
 * written in seconds from ten seconds up, and "under 1 ms" for a reading that
 * rounds to nothing.
 */
describe('durationWords', () => {
  it('says a reading under a millisecond as that', () => {
    expect(durationWords(0)).toBe('under 1 ms');
    expect(durationWords(0.4)).toBe('under 1 ms');
  });

  it('writes milliseconds grouped by thousands, one decimal place kept', () => {
    expect(durationWords(1)).toBe('1 ms');
    expect(durationWords(298.4)).toBe('298.4 ms');
    expect(durationWords(1183)).toBe('1,183 ms');
    expect(durationWords(9999.9)).toBe('9,999.9 ms');
  });

  it('switches to seconds at ten seconds and over', () => {
    expect(durationWords(SECONDS_FROM_MS)).toBe('10 s');
    expect(durationWords(68479.1)).toBe('68.5 s');
    expect(durationWords(194912)).toBe('194.9 s');
    expect(durationWords(12_345_678)).toBe('12,345.7 s');
  });
});
