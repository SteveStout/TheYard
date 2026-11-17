/**
 * Does:      Holds the badge's answer (gateFacts.ts): green only when the gate's own reading of the running version
 *            is the history's newest, the quality held against the reading before it, and CodeRabbit's read named
 *            only when the gate wrote a full one.
 * Does not:  Draw the badge; gate-badge.spec.ts does.
 * Used by:   Vitest (npm test).
 */
import { describe, expect, it } from 'vitest';
import type { HistoryRow } from '../../../lib/qualityHeld';
import { gateChips, gateFacts, qualityLine } from './gateFacts';

const row = (version: string, a: number, commit = 'abcdef0123'): HistoryRow => ({
  version,
  date: '2026-11-17',
  commit,
  source: 'quality.py',
  numbers: { a, b: 1 },
});
const facts = (version: string, reviewed: string | null) => ({
  days: {},
  version,
  builtOn: '2026-11-17',
  review: reviewed === null ? null : { version, reviewed, critical: 0 },
});

describe('the gate badge', () => {
  it('is green with the quality held and the review when the gate measured this version', () => {
    const answer = gateFacts([row('1.0.0.1', 5), row('1.0.0.2', 4)], facts('1.0.0.2', 'full'));
    expect(answer).toMatchObject({
      gated: true,
      commit: 'abcdef0',
      quality: { held: 2, of: 2, better: 1 },
    });
    expect(gateChips(answer)).toEqual(['Gate green', 'Quality 2 of 2 held', 'CodeRabbit reviewed']);
  });

  it('claims nothing for a version the gate has not measured or CodeRabbit has not read', () => {
    const answer = gateFacts([row('1.0.0.1', 5)], facts('1.0.0.2', null));
    expect(answer.gated).toBe(false);
    expect(gateChips(answer)).toEqual(['Gate pending', 'Quality pending', 'CodeRabbit pending']);
    expect(
      gateChips({ ...answer, review: { version: '1.0.0.2', reviewed: 'none', critical: 0 } })[2]
    ).toBe('CodeRabbit not reviewed');
  });

  it('says none worse only when every number held (CodeRabbit on 1.0.5.82)', () => {
    expect(qualityLine({ held: 2, of: 2, better: 1 })).toBe(
      '2 of 2 quality numbers held, none worse than in the version before, 1 better'
    );
    expect(qualityLine({ held: 1, of: 2, better: 0 })).toBe(
      '1 of 2 quality numbers held, 1 worse than in the version before'
    );
  });
});
