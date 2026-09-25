import { describe, expect, it } from 'vitest';
import { gaugeMeter } from './gauge';

describe('gaugeMeter', () => {
  it('draws the share of the ceiling and reads the same number', () => {
    expect(gaugeMeter(364, 1183)).toEqual({ share: 364 / 1183, now: 364, max: 1183 });
  });

  it('stops at a full bar for a reading over its ceiling', () => {
    expect(gaugeMeter(1500, 1000)).toEqual({ share: 1, now: 1000, max: 1000 });
  });

  it('reads nothing, never a negative or NaN, for a reading that is not a number or below zero', () => {
    expect(gaugeMeter(Number.NaN, 100)).toEqual({ share: 0, now: 0, max: 100 });
    expect(gaugeMeter(-4, 100)).toEqual({ share: 0, now: 0, max: 100 });
  });

  it('draws an empty bar when there is no ceiling to measure against', () => {
    expect(gaugeMeter(12, 0)).toEqual({ share: 0, now: 0, max: 0 });
    expect(gaugeMeter(12, Number.NaN)).toEqual({ share: 0, now: 0, max: 0 });
  });
});
