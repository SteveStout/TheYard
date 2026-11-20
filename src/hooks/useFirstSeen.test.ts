import { describe, expect, it } from 'vitest';
import { SEEN_AT, SEEN_CLASS, seenNow } from './useFirstSeen';

describe('useFirstSeen: when a motion plays (sales1120)', () => {
  it('plays once a quarter of its holder is in view, and stops when it leaves', () => {
    expect(SEEN_AT).toBe(0.25);
    expect(SEEN_CLASS).toBe('is-seen');
    expect(seenNow(0.25, false)).toBe(true);
    expect(seenNow(1, false)).toBe(true);
    expect(seenNow(0.24, false)).toBe(false);
    expect(seenNow(0, false)).toBe(false);
  });

  it('never plays for a reader who asked for reduced motion: the finished state shows', () => {
    expect(seenNow(1, true)).toBe(false);
    expect(seenNow(0.5, true)).toBe(false);
  });
});
