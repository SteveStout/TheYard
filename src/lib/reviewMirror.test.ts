import { describe, expect, it } from 'vitest';
import type { TestSummary } from './landingProof';
import type { Recommendation } from './recommendations';
import {
  GATE_ADDRESS,
  GATE_PARTS,
  gateTotals,
  MIRROR,
  reviewedWord,
  screenOf,
  stripLabel,
  stripOf,
} from './reviewMirror';

const entry = (name: string): Recommendation => ({
  name,
  title: `${name}'s title as the API gives it`,
  profile: 'https://www.linkedin.com/in/someone/',
  relationship: 'worked with',
  when: 'May 2024',
  paragraphs: ['Words.'],
  pull: 'A sentence.',
});

const summary: TestSummary = {
  version: '1.0.5.108',
  ran_at: '2026-11-19T17:00:00Z',
  gate_seconds: 900,
  passed: 3000,
  failed: 2,
  skipped: 0,
  suites: [
    { id: 'vitest', name: 'Vitest', passed: 700, failed: 0, skipped: 0, seconds: 30 },
    { id: 'xunit-sqlite', name: 'xUnit', passed: 2000, failed: 2, skipped: 0, seconds: 300 },
    { id: 'browser-sqlite', name: 'Browser', passed: 300, failed: 0, skipped: 0, seconds: 600 },
  ],
};

describe('the review route mirror (ADR: The review route gives a reason before it asks, the addendum on the mirror)', () => {
  it('is drawn only for for=ai; any other value, or none, draws the verdict', () => {
    expect(screenOf('for=ai')).toBe('mirror');
    expect(screenOf('for=AI')).toBe('verdict');
    expect(screenOf('for=sales')).toBe('verdict');
    expect(screenOf('')).toBe('verdict');
  });

  it('says the design’s words, word for word, and its primary move opens the tests card', () => {
    expect(MIRROR.title).toBe('Shipping with AI without losing quality.');
    expect(MIRROR.eyebrow).toBe('For the manager hiring a remote Lead or Staff engineer');
    expect(MIRROR.primary).toBe('See what this version had to pass');
    expect(GATE_ADDRESS).toBe('/?view=admin&card=tests');
    expect(GATE_PARTS).toHaveLength(6);
    expect(GATE_PARTS[5]).toBe('CodeRabbit on the pull request');
  });

  it('prints the landing page’s tests figure, the failures among the suites counted once, and the summary’s version', () => {
    expect(gateTotals(summary, '1.0.5.107')).toEqual({
      passed: '3,000',
      failed: '2',
      version: '1.0.5.108',
    });
    expect(gateTotals(null, '1.0.5.107')).toEqual({ passed: '', failed: '', version: '1.0.5.107' });
    expect(reviewedWord({ reviewed: 'full' })).toBe('reviewed');
    expect(reviewedWord({ reviewed: 'partial' })).toBe('not reviewed');
    expect(reviewedWord(null)).toBe('pending');
  });

  it('names the strip in its order with the API’s titles, skips a name the API lacks, and counts the rest', () => {
    const list = ['Bill Sladek', 'Meagan Colley', 'Drew Dell', 'Lee Markum', 'Sean Whitesell'].map(
      entry
    );
    const strip = stripOf(list);
    expect(strip.shown.map((each) => each.name)).toEqual([
      'Drew Dell',
      'Sean Whitesell',
      'Meagan Colley',
    ]);
    expect(strip.shown[0].title).toBe("Drew Dell's title as the API gives it");
    expect(strip.more).toBe(2);
    expect(strip.count).toBe(5);
    expect(stripLabel(13)).toBe('13 people who worked with him put their name on it');
    expect(stripOf(null)).toEqual({ shown: [], more: 0, count: 0 });
  });
});
