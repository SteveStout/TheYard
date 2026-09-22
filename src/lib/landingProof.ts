/**
 * The landing page's evidence strip (1.0.2.0): the four figures a reader who
 * has never seen this project is asked to believe, each one read from
 * something the build itself produced rather than typed into a page.
 *
 * The counts come from /api/tests/summary, which is the gate's own results
 * file added up; the record count is the site map's own list of decision
 * records, so it cannot drift from the records the sidebar opens. A suite the
 * gate carried forward from an earlier version is named as carried, because a
 * number this build did not produce is not this build's evidence.
 *
 * No React here.
 */

export type TestSuiteCount = {
  id: string;
  name: string;
  passed: number;
  failed: number;
  skipped: number;
  seconds: number;
  carried?: string | null;
};

export type TestSummary = {
  version: string;
  ran_at: string;
  gate_seconds: number;
  passed: number;
  failed: number;
  skipped: number;
  suites: TestSuiteCount[];
};

export type ProofFigure = { key: string; figure: string; label: string; detail: string };

/** The store passes rerun the same tests on the other store, so the headline count is the gate's own once. */
const COUNTED_ONCE = ['vitest', 'xunit-sqlite', 'xunit-live', 'browser-sqlite'];

const plural = (count: number, one: string) => `${count} ${one}${count === 1 ? '' : 's'}`;

/** A whole number with thousands separated, in the one locale the site writes in. */
export function figures(count: number): string {
  return count.toLocaleString('en-US');
}

/**
 * The four figures, or null while the summary has not arrived, so the strip
 * reserves its box rather than jumping when it does.
 */
export function proofFigures(summary: TestSummary | null, records: number): ProofFigure[] | null {
  if (summary === null) return null;
  const counted = summary.suites.filter((suite) => COUNTED_ONCE.includes(suite.id));
  const tests = counted.reduce((total, suite) => total + suite.passed, 0);
  const failed = summary.suites.reduce((total, suite) => total + suite.failed, 0);
  const carried = summary.suites.filter((suite) => suite.carried);
  const stores = summary.suites.filter(
    (suite) => suite.id === 'xunit-cosmos' || suite.id === 'browser-cosmos'
  );
  return [
    {
      key: 'tests',
      figure: figures(tests),
      label: failed === 0 ? 'tests green' : `tests, ${plural(failed, 'failure')}`,
      detail: 'xUnit, Vitest and Playwright, every version',
    },
    {
      key: 'gate',
      figure: `${summary.gate_seconds} s`,
      label: 'one gate',
      detail:
        carried.length === 0
          ? `every suite ran for ${summary.version}`
          : `${plural(carried.length, 'suite')} carried from ${carried[0].carried}`,
    },
    {
      key: 'records',
      figure: figures(records),
      label: 'decision records',
      detail: 'every choice, and what it cost',
    },
    {
      key: 'stores',
      figure: stores.length > 0 ? '2' : '1',
      label: stores.length > 0 ? 'stores, one codebase' : 'store',
      detail: 'Azure SQL Database and Azure Cosmos DB',
    },
  ];
}
