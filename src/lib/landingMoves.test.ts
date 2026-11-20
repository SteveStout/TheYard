import { describe, expect, it } from 'vitest';
import {
  DOCK_REVIEW,
  LANDING_CLOSE,
  MOVES,
  newestBadge,
  readAllLine,
  START_HERE,
} from './landingMoves';

describe('the home page’s own words (ADR-082, the addendum on the home page’s order)', () => {
  it('words the moves, Start here, the close and the dock as the picture does', () => {
    expect(MOVES).toEqual({
      primary: 'Check my work in 2 minutes',
      resume: 'Resume (PDF)',
      inventory: 'or browse the live auction',
    });
    expect(START_HERE).toBe('Start here');
    expect(LANDING_CLOSE).toEqual({
      readout: 'Next step',
      line: 'Twenty minutes, by email or on LinkedIn.',
    });
    expect(DOCK_REVIEW).toBe('Check my work');
    expect(readAllLine(13)).toBe('Read all 13 named recommendations');
    expect(readAllLine(null)).toBe('Read all named recommendations');
  });

  it('calls the newest post new for a week, by the sidebar’s own rule, and the latest after that', () => {
    const now = Date.parse('2026-11-19T23:00:00Z');
    expect(newestBadge('2026-11-19T12:00:00Z', now)).toBe('New post');
    expect(newestBadge('2026-11-05T12:00:00Z', now)).toBe('Latest post');
    expect(newestBadge(undefined, now)).toBe('Latest post');
  });
});
