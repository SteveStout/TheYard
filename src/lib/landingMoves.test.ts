import { describe, expect, it } from 'vitest';
import {
  DOCK_REVIEW,
  LANDING_CLOSE,
  MOVES,
  newestBadge,
  readAllLine,
  SECTION_NUMBERS,
  START_HERE,
} from './landingMoves';
import { CLOSE } from './reviewVerdict';

describe('the home page’s own words (ADR-082, the addendum on the home page’s order)', () => {
  it('words the four moves, Start here, the close and the dock as the picture does', () => {
    expect(Object.values(MOVES)).toEqual([
      'Check my work in 2 minutes',
      'Resume (PDF)',
      'Book 20 minutes',
      'or browse the live auction',
    ]);
    expect(START_HERE).toBe('Start here');
    expect(LANDING_CLOSE).toEqual({
      label: 'Next step',
      number: '08',
      question: 'What has to work, where is it today, and what does success look like?',
      line: 'Book 20 minutes opens an email to me with those three questions in it, and I answer with a time.',
    });
    expect(DOCK_REVIEW).toBe('Check my work');
    expect(readAllLine(13)).toBe('Read all 13 named recommendations');
    expect(readAllLine(null)).toBe('Read all named recommendations');
  });

  it('asks the review route’s own question in the close, under the number after the seven sections', () => {
    expect(LANDING_CLOSE.question).toBe(CLOSE.question);
    expect(LANDING_CLOSE.line).toBe(CLOSE.line);
    expect(LANDING_CLOSE.label).toBe(CLOSE.label);
    expect(Object.values(SECTION_NUMBERS)).toEqual([
      '01',
      '02',
      '03',
      '04',
      '05',
      '06',
      '07',
      '08',
    ]);
    expect(Object.keys(SECTION_NUMBERS)).toEqual([
      'start',
      'posts',
      'voices',
      'stack',
      'built',
      'run',
      'who',
      'close',
    ]);
  });

  it('calls the newest post new for a week, by the sidebar’s own rule, and the latest after that', () => {
    const now = Date.parse('2026-11-19T23:00:00Z');
    expect(newestBadge('2026-11-19T12:00:00Z', now)).toBe('New post');
    expect(newestBadge('2026-11-05T12:00:00Z', now)).toBe('Latest post');
    expect(newestBadge(undefined, now)).toBe('Latest post');
  });
});
