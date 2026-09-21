import { describe, expect, it } from 'vitest';
import {
  duration,
  outcomeWord,
  rowsOf,
  totals,
  type TestResults,
  type TestSuite,
} from './testResults';

const suite: TestSuite = {
  id: 'xunit-sqlite',
  name: 'xUnit on SQLite',
  seconds: 12,
  passed: 3,
  failed: 1,
  skipped: 0,
  tests: [
    ['AuthTests', 'Sign in', 'p', 40],
    ['BidRulesTests', 'A bid under the reserve', 'p', 900],
    ['AuthTests', 'Lockout', 'f', 5],
    ['PageStatusTests', 'Every address answers', 'p', 3200],
  ],
};

const results: TestResults = {
  version: '1.0.0.173',
  ranAt: '2026-09-21T17:40:00Z',
  gateSeconds: 400,
  checks: [{ name: 'Prettier', passed: true, seconds: 9 }],
  suites: [suite, { ...suite, id: 'vitest', name: 'Vitest', failed: 0, passed: 2, tests: [] }],
};

describe('the gate’s test results', () => {
  it('adds every suite’s counts together', () => {
    expect(totals(results)).toEqual({ tests: 4, passed: 5, failed: 1, skipped: 0 });
  });

  it('lists failures first, then the slowest, or by group and name', () => {
    expect(rowsOf(suite, '', 'slowest').map((row) => row[1])).toEqual([
      'Lockout',
      'Every address answers',
      'A bid under the reserve',
      'Sign in',
    ]);
    expect(rowsOf(suite, '', 'name').map((row) => row[1])).toEqual([
      'Lockout',
      'Sign in',
      'A bid under the reserve',
      'Every address answers',
    ]);
  });

  it('keeps only the rows whose group or name holds every word of the filter', () => {
    expect(rowsOf(suite, 'auth sign', 'name').map((row) => row[1])).toEqual(['Sign in']);
    expect(rowsOf(suite, 'PAGESTATUS', 'name')).toHaveLength(1);
    expect(rowsOf(suite, 'nothing like this', 'name')).toHaveLength(0);
  });

  it('prints a time in milliseconds under a second and seconds above, and an outcome as a word', () => {
    expect(duration(40)).toBe('40 ms');
    expect(duration(3200)).toBe('3.2 s');
    expect(outcomeWord('p')).toBe('passed');
    expect(outcomeWord('f')).toBe('failed');
    expect(outcomeWord('s')).toBe('skipped');
  });
});
