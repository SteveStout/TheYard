/**
 * Does:      Holds the one meaning of "held" (qualityHeld.ts): a number is no worse than the version before, a number
 *            named better-higher may not fall, a missing reading is not a slip, and the run starts after the last slip.
 * Does not:  Read the live history for its cases; the last case only checks the live history gives an answer.
 * Used by:   Vitest (npm test).
 */
import { describe, expect, it } from 'vitest';
import history from '../../data/quality-history.json';
import { heldAt, heldSince, measured, type HistoryRow } from './qualityHeld';

const row = (
  version: string,
  numbers: HistoryRow['numbers'],
  source = 'quality.py'
): HistoryRow => ({
  version,
  date: `2026-11-${version.split('.').at(-1)!.padStart(2, '0')}`,
  source,
  numbers,
});

describe('quality held', () => {
  it('holds a number that stays or falls, and one named better-higher that stays or rises', () => {
    const before = row('1.0.0.1', { files_over_300: 5, dotnet_tests_higher_is_better: 100 });
    expect(
      heldAt(row('1.0.0.2', { files_over_300: 4, dotnet_tests_higher_is_better: 101 }), before)
    ).toEqual({
      held: 2,
      of: 2,
      better: 2,
    });
    expect(
      heldAt(row('1.0.0.2', { files_over_300: 6, dotnet_tests_higher_is_better: 99 }), before)
    ).toEqual({
      held: 0,
      of: 2,
      better: 0,
    });
  });

  it('counts a missing reading as neither held against nor a slip, and the first version as all held', () => {
    expect(heldAt(row('1.0.0.2', { a: 3, b: null }), row('1.0.0.1', { a: 3 }))).toEqual({
      held: 1,
      of: 1,
      better: 0,
    });
    expect(heldAt(row('1.0.0.1', { a: 3, b: 4 }), undefined)).toEqual({
      held: 2,
      of: 2,
      better: 0,
    });
  });

  it('starts the run after the last version that made any number worse, and is null when the newest did', () => {
    const rows = [
      row('1.0.0.1', { a: 3 }),
      row('1.0.0.2', { a: 4 }),
      row('1.0.0.3', { a: 2 }),
      row('1.0.0.4', { a: 2 }),
    ];
    expect(heldSince(rows)).toEqual({ version: '1.0.0.3', date: '2026-11-03', releases: 2 });
    expect(heldSince([...rows, row('1.0.0.5', { a: 9 })])).toBeNull();
  });

  it('leaves a pre-scan reading out of the versions measured', () => {
    expect(
      measured([row('', { a: 1 }, 'pre-scan'), row('1.0.0.1', { a: 1 })]).map(
        (each) => each.version
      )
    ).toEqual(['1.0.0.1']);
  });

  it('answers from the history the build carries', () => {
    expect(heldSince(measured(history.rows as unknown as HistoryRow[]))).not.toBeNull();
  });
});
