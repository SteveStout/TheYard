import { describe, expect, it } from 'vitest';
import { figures, proofFigures, type TestSummary } from './landingProof';

const summary: TestSummary = {
  version: '1.0.1.6',
  ran_at: '2026-09-22T16:06:34.243Z',
  gate_seconds: 312,
  passed: 1515,
  failed: 0,
  skipped: 0,
  suites: [
    { id: 'vitest', name: 'Vitest', passed: 224, failed: 0, skipped: 0, seconds: 4 },
    {
      id: 'xunit-sqlite',
      name: 'xUnit on SQLite',
      passed: 581,
      failed: 0,
      skipped: 0,
      seconds: 26,
    },
    {
      id: 'xunit-live',
      name: 'The live Cosmos DB tests',
      passed: 7,
      failed: 0,
      skipped: 0,
      seconds: 43,
    },
    {
      id: 'xunit-cosmos',
      name: 'xUnit on Cosmos DB',
      passed: 581,
      failed: 0,
      skipped: 0,
      seconds: 102,
      carried: '1.0.1.4',
    },
    {
      id: 'browser-sqlite',
      name: 'Browser on SQLite',
      passed: 93,
      failed: 0,
      skipped: 0,
      seconds: 149,
    },
    {
      id: 'browser-cosmos',
      name: 'Browser on Cosmos DB',
      passed: 29,
      failed: 0,
      skipped: 0,
      seconds: 102,
      carried: '1.0.1.4',
    },
  ],
};

describe('the landing page evidence strip', () => {
  it('counts a test once, not once per store, and says so in words a reader checks', () => {
    const shown = proofFigures(summary, 82)!;
    expect(shown.map((figure) => [figure.figure, figure.label])).toEqual([
      ['905', 'tests green'],
      ['312 s', 'one gate'],
      ['82', 'decision records'],
      ['2', 'stores, one codebase'],
    ]);
    // The store passes rerun the same tests, so they are not added to the headline.
    expect(224 + 581 + 7 + 93).toBe(905);
  });

  it('names a carried suite rather than passing its count off as this build', () => {
    expect(proofFigures(summary, 82)![1].detail).toBe('2 suites carried from 1.0.1.4');
    const fresh = {
      ...summary,
      suites: summary.suites.map((suite) => ({ ...suite, carried: undefined })),
    };
    expect(proofFigures(fresh, 82)![1].detail).toBe('every suite ran for 1.0.1.6');
  });

  it('says how many failed rather than calling a red gate green', () => {
    const red = {
      ...summary,
      suites: summary.suites.map((suite) =>
        suite.id === 'vitest' ? { ...suite, passed: 223, failed: 1 } : suite
      ),
    };
    expect(proofFigures(red, 82)![0].label).toBe('tests, 1 failure');
  });

  it('reserves its box until the answer arrives, and writes a thousand as a reader does', () => {
    expect(proofFigures(null, 82)).toBeNull();
    expect(figures(1515)).toBe('1,515');
  });
});
