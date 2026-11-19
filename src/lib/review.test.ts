import { describe, expect, it } from 'vitest';
import {
  countedLine,
  figureWords,
  funnelBars,
  screenLine,
  secondCountedLine,
  secondFigures,
  reachedWords,
  REORDERED_LINE,
  REVIEW_REORDERED,
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
      '1 Quality you can watch',
      '2 The first screen',
      '3 Bid in the room',
      '4 What Azure charges',
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
    expect(reachedWords(2)).toBe('Stop 2, The first screen');
    expect(reachedWords(0)).toBe('Opened, left');
    expect(reachedWords(9)).toBe('Opened, left');
    expect(tookWords(0)).toBe('under 1 min');
    expect(tookWords(11)).toBe('11 min');
  });

  it('names the day counting began and never reads a bare count as all time', () => {
    expect(countedLine('2026-11-04')).toBe('Counting from 2026-11-04, the day version B shipped.');
    expect(countedLine(null)).not.toMatch(/all time/);
  });

  it('names the day the stops were reordered, so reach counted before it is read against the earlier order', () => {
    expect(REVIEW_REORDERED).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(REORDERED_LINE).toContain(`reordered on ${REVIEW_REORDERED}`);
    expect(REORDERED_LINE).toContain('Quality you can watch became stop 1');
    expect(REORDERED_LINE).toContain('counted against the earlier order');
  });

  it('gives the two figures as a count and a whole share of the visitors who opened', () => {
    const second = { second_action: 3, running_or_book: 1, counted_from: '2026-11-19' };
    const figures = secondFigures({ opened: 8, second });
    expect(figures.map((figure) => [figure.label, figure.count, figure.percent])).toEqual([
      ['reached a second action', 3, 38],
      ['reached the running system or the booking button', 1, 13],
    ]);
    expect(figureWords(figures[0], 8)).toBe('3 of 8 who opened, 38%');
    expect(secondCountedLine(second)).toBe(
      'These two figures count from 2026-11-19, the day the buttons started reporting.'
    );
    // Nobody opened, or an answer made before the figures existed: none, never a division by nothing.
    expect(secondFigures({ opened: 0, second }).map((figure) => figure.percent)).toEqual([0, 0]);
    expect(secondFigures({ opened: 5 }).map((figure) => figure.count)).toEqual([0, 0]);
    expect(secondCountedLine(undefined)).toBe('The two figures are not counted on this version.');
  });

  it('splits the two figures by first screen, each a whole share of who opened on it', () => {
    expect(screenLine({ screen: 'verdict', opened: 8, second_action: 3, running_or_book: 1 })).toBe(
      'The verdict, /?view=review: 8 opened, 38% a second action, 13% the running system or the booking button'
    );
    expect(screenLine({ screen: 'mirror', opened: 0, second_action: 0, running_or_book: 0 })).toBe(
      'The mirror, /?view=review&for=ai: 0 opened, 0% a second action, 0% the running system or the booking button'
    );
  });

  it('never gives a figure above the visitors who opened, whatever the answer says', () => {
    const second = { second_action: 9, running_or_book: -2, counted_from: '2026-11-19' };
    expect(
      secondFigures({ opened: 4, second }).map((figure) => [figure.count, figure.percent])
    ).toEqual([
      [4, 100],
      [0, 0],
    ]);
  });
});
