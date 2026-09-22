/**
 * Every test result the ship's gate produced for this build, as the Admin
 * tab's card reads it (ADR: The five-minute gate, the addendum on every check
 * running once). The gate writes data/test-results.json with
 * scripts/test_results.mjs and the API serves it at /api/admin/tests.
 *
 * No React in here: the file's shape, the sums the card states, and the order
 * and filter the list is shown in.
 */

/** One test: its group (a class or a file), its name, p f or s, and its milliseconds. */
export type TestRow = [group: string, name: string, outcome: 'p' | 'f' | 's', ms: number];

export type TestSuite = {
  id: string;
  name: string;
  seconds: number;
  passed: number;
  failed: number;
  skipped: number;
  /** Set when this build's gate did not run the suite and carried the rows
   * forward from the version named here, whose gate did. */
  carried?: string;
  tests: TestRow[];
};

export type TestCheck = { name: string; passed: boolean; seconds: number };

export type TestResults = {
  version: string;
  ranAt: string;
  gateSeconds: number;
  checks: TestCheck[];
  suites: TestSuite[];
};

export type TestTotals = { tests: number; passed: number; failed: number; skipped: number };

/** The whole gate's counts, every suite together. */
export function totals(results: TestResults): TestTotals {
  return results.suites.reduce(
    (sum, suite) => ({
      tests: sum.tests + suite.tests.length,
      passed: sum.passed + suite.passed,
      failed: sum.failed + suite.failed,
      skipped: sum.skipped + suite.skipped,
    }),
    { tests: 0, passed: 0, failed: 0, skipped: 0 }
  );
}

export type TestOrder = 'slowest' | 'name';

/**
 * A suite's rows as the card lists them: failures first, because they are the
 * rows anybody needs to read, then by time or by name, and only the rows whose
 * group or name holds every word of the filter.
 */
export function rowsOf(suite: TestSuite, filter: string, order: TestOrder): TestRow[] {
  const words = filter.toLowerCase().split(/\s+/).filter(Boolean);
  const kept = suite.tests.filter((row) => {
    const text = `${row[0]} ${row[1]}`.toLowerCase();
    return words.every((word) => text.includes(word));
  });
  const rank = (row: TestRow) => (row[2] === 'f' ? 0 : row[2] === 's' ? 1 : 2);
  return [...kept].sort(
    (a, b) =>
      rank(a) - rank(b) ||
      (order === 'slowest' ? b[3] - a[3] : a[0].localeCompare(b[0]) || a[1].localeCompare(b[1]))
  );
}

/** A test's time as the card prints it: milliseconds under a second, seconds above. */
export function duration(ms: number): string {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
}

/** The word a row's outcome is shown as. */
export function outcomeWord(outcome: TestRow[2]): string {
  return outcome === 'p' ? 'passed' : outcome === 'f' ? 'failed' : 'skipped';
}
