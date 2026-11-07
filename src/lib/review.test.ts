import { describe, expect, it } from 'vitest';
import {
  countedLine,
  funnelBars,
  reachedWords,
  REVIEW_WINDOWS,
  tookWords,
  windowLabel,
} from './review';

describe('the review route card (ADR: Ten minutes, seven stops)', () => {
  it('offers four windows, the three every Admin chart offers and the links card’s ninety days', () => {
    expect(REVIEW_WINDOWS).toEqual(['24h', '7d', '30d', '90d']);
    expect(REVIEW_WINDOWS.map(windowLabel)).toEqual(['24 h', '7 d', '30 d', '90 d']);
  });

  it('draws eight bars, opened then the seven stops by title, each a share of the most', () => {
    const bars = funnelBars({ reached_by_stop: [40, 32, 20, 10, 10, 5, 4, 2] });
    expect(bars).toHaveLength(8);
    expect(bars.map((bar) => bar.name)).toEqual([
      'Opened /review',
      '1 The first screen',
      '2 Bid in the room',
      '3 What Azure charges',
      '4 One decision, with its code',
      '5 The gate that shipped this version',
      '6 One codebase, two stores',
      '7 The resume, and who vouches for it',
    ]);
    expect(bars[0].share).toBe(1);
    expect(bars[3].share).toBe(0.25);
    // An answer short of eight places reads the missing ones as none, and none draws no bar.
    const short = funnelBars({ reached_by_stop: [3] });
    expect(short).toHaveLength(8);
    expect(short.slice(1).every((bar) => bar.count === 0 && bar.share === 0)).toBe(true);
    expect(funnelBars({ reached_by_stop: [] }).every((bar) => bar.share === 0)).toBe(true);
  });

  it('says how far a code got and how long it took, in the card’s words', () => {
    expect(reachedWords(7)).toBe('Stop 7, The resume, and who vouches for it');
    expect(reachedWords(2)).toBe('Stop 2, Bid in the room');
    expect(reachedWords(0)).toBe('Opened, left');
    expect(reachedWords(9)).toBe('Opened, left');
    expect(tookWords(0)).toBe('under 1 min');
    expect(tookWords(11)).toBe('11 min');
  });

  it('names the day counting began and never reads a bare count as all time', () => {
    expect(countedLine('2026-11-04')).toBe('Counting from 2026-11-04, the day version B shipped.');
    expect(countedLine(null)).not.toMatch(/all time/);
  });
});
