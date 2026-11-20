import { describe, expect, it } from 'vitest';
import type { TestSummary } from './landingProof';
import { proofFigures } from './landingProof';
import type { Recommendation } from './recommendations';
import { REVIEW_ACTIONS } from './reviewProgress';
import {
  CLOSE_MOVES,
  leadQuote,
  liveSentence,
  PROOF_ADDRESS,
  SLIP_ADDRESS,
  RECORD_TILES,
  recommendationsLink,
  restLine,
  tourSeen,
  TOUR,
  testsTile,
  TILE_DOOR,
  VERDICT,
} from './reviewVerdict';

const entry = (name: string): Recommendation => ({
  name,
  title: `${name}'s title`,
  profile: 'https://www.linkedin.com/in/someone/',
  relationship: 'worked with',
  when: 'May 2024',
  paragraphs: ['Words.'],
  pull: `${name} said so.`,
});

const summary: TestSummary = {
  version: '1.0.5.107',
  ran_at: '2026-11-19T17:00:00Z',
  gate_seconds: 900,
  passed: 6140,
  failed: 0,
  skipped: 0,
  suites: [
    { id: 'vitest', name: 'Vitest', passed: 722, failed: 0, skipped: 0, seconds: 30 },
    { id: 'xunit-sqlite', name: 'xUnit', passed: 2535, failed: 0, skipped: 0, seconds: 300 },
    { id: 'xunit-live', name: 'live', passed: 12, failed: 0, skipped: 0, seconds: 10 },
    { id: 'xunit-cosmos', name: 'Cosmos', passed: 2535, failed: 0, skipped: 0, seconds: 300 },
    { id: 'browser-sqlite', name: 'Browser', passed: 276, failed: 0, skipped: 0, seconds: 600 },
  ],
};

describe('the review route first screen (ADR: The review route gives a reason before it asks)', () => {
  it('says the spec’s words, word for word', () => {
    expect(VERDICT.title.join(' ')).toBe(
      'Production .NET that stays up. Built with AI, measured on every version.'
    );
    expect(VERDICT.eyebrow.join(' / ')).toBe('Steven Stout / Lead / Staff .NET engineer / Remote');
    expect(VERDICT.primary).toBe('See the work');
    expect(VERDICT.book).toBe('Book 20 minutes');
    expect(VERDICT.roles).toBe('Open to remote Lead, Staff and Principal roles.');
    // The ten-minute ask is only below the fold: the words above it never name it.
    const above = [
      ...VERDICT.eyebrow,
      ...VERDICT.title,
      VERDICT.lede,
      VERDICT.primary,
      VERDICT.book,
      VERDICT.quoteLabel,
      VERDICT.roles,
      ...RECORD_TILES.flatMap((tile) => [tile.figure, tile.label, tile.source]),
    ].join(' ');
    expect(above).not.toMatch(/ten minutes|seven stops/i);
  });

  it('opens the top of Quality for the proof, and the mirror’s slip line at the slip and the turn', () => {
    expect(PROOF_ADDRESS).toBe('/?doc=quality');
    expect(SLIP_ADDRESS).toBe('/?doc=quality#the-slip-and-the-turn');
  });

  it('prints the same tests figure the landing page prints, and the quality held only when the build has it', () => {
    const landing = proofFigures(summary, 0)?.find((figure) => figure.key === 'tests')?.figure;
    expect(testsTile(summary, { held: 15, of: 15 })).toMatchObject({
      figure: landing,
      label: 'tests passed on the version you are reading',
      source: 'TheYard, read from the build. 15 of 15 quality numbers held.',
      live: true,
    });
    expect(landing).toBe('3,545');
    expect(testsTile(summary, null).source).toBe('TheYard, read from the build.');
    // Before the summary arrives the tile keeps its place with no figure.
    expect(testsTile(null, null).figure).toBe('');
  });

  it('makes each number tile a door to its case card, in tile order, and says no line under the buttons (sales1120)', () => {
    expect([...RECORD_TILES, testsTile(null, null)].map((tile) => tile.door)).toEqual([
      'case-conway',
      'case-storee',
      'case-theyard',
    ]);
    expect(TILE_DOOR).toBe('See how');
    expect(Object.keys(VERDICT)).not.toContain('under');
  });

  it('quotes Drew Dell when the answer has him, the first entry when not, and nobody for an empty answer', () => {
    const list = [entry('Cliff True'), entry('Drew Dell'), entry('Henry Crans')];
    expect(leadQuote(list)?.name).toBe('Drew Dell');
    expect(leadQuote([entry('Cliff True')])?.name).toBe('Cliff True');
    expect(leadQuote([])).toBeNull();
    expect(leadQuote(null)).toBeNull();
    expect(recommendationsLink(13)).toBe('Read all 13 named recommendations');
  });

  it('words the tour as the whole-page picture does, with its phone control', () => {
    expect(TOUR.title).toBe('The full tour, if you want it');
    expect(TOUR.line).toBe(
      'Seven stops, ten minutes, all optional. Every stop is the running system, and your place is kept on this browser.'
    );
    expect(tourSeen(0, 7)).toBe('0 of 7 seen.');
    expect(tourSeen(3, 7)).toBe('3 of 7 seen.');
    expect(TOUR.show).toBe('Show the seven stops');
    expect(TOUR.hide).toBe('Hide the seven stops');
  });

  it('says what the sidebar holds and the store bar’s one sentence, from the build’s own facts', () => {
    expect(restLine(138, 19)).toBe(
      '138 decision records, 19 diagrams, the incidents and the changelog.'
    );
    expect(liveSentence(true)).toEqual({
      lead: 'Live system.',
      rest: 'This version passed every check before it shipped.',
    });
    expect(liveSentence(false)).toEqual({ lead: 'Live system.', rest: null });
  });

  it('counts the tile from the suites the landing counts once, so a failure there still leaves the passed figure', () => {
    const failing: TestSummary = {
      ...summary,
      failed: 3,
      suites: summary.suites.map((suite) =>
        suite.id === 'vitest' ? { ...suite, passed: 719, failed: 3 } : suite
      ),
    };
    const landing = proofFigures(failing, 0)?.find((figure) => figure.key === 'tests')?.figure;
    expect(testsTile(failing, { held: 14, of: 15 })).toMatchObject({
      figure: landing,
      source: 'TheYard, read from the build. 14 of 15 quality numbers held.',
    });
    expect(landing).toBe('3,542');
  });
});

describe('the close’s three moves, which the Quality sheet’s foot draws too (saleshome1120)', () => {
  it('are Book 20 minutes, the gold resume and LinkedIn, in that order, each reporting its own action', () => {
    expect(CLOSE_MOVES.map((move) => move.label)).toEqual([
      'Book 20 minutes',
      'Resume (PDF)',
      'Message me on LinkedIn',
    ]);
    expect(CLOSE_MOVES.map((move) => move.href)).toEqual([
      '/api/go/book',
      '/resume?pdf=1',
      'https://www.linkedin.com/in/stevenwstout/',
    ]);
    expect(CLOSE_MOVES.map((move) => move.look)).toEqual([
      'op-pill-primary',
      'op-pill-resume',
      'op-pill-secondary',
    ]);
    expect(CLOSE_MOVES.map((move) => move.action)).toEqual(['book', 'resume', 'linkedin']);
    for (const move of CLOSE_MOVES) expect(REVIEW_ACTIONS).toContain(move.action);
  });
});
