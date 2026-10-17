/**
 * The landing page's evidence strip: the three figures a reader who
 * has never seen this project is asked to believe, each one read from
 * something the build itself produced rather than typed into a page.
 *
 * The gate's own seconds are not among them any more. To a stranger a number
 * beside a target reads as a miss before it reads as a gate, and the target is
 * held by a test and shown on the Admin tab, where the card has room to say
 * what it is (ADR: One reading for the first screen).
 *
 * The tests tile says so when a suite was carried from an earlier version rather than measured
 * for this one, which is where the gate tile used to say it.
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

export type ProofFigure = {
  key: string;
  figure: string;
  label: string;
  detail: string;
  /** A ring beside the figure where it is a share of a whole (the operator's look). */
  ring?: ProofRing;
};

/** A share of a whole for a ring, and the words a screen reader hears for it. */
export type ProofRing = { value: number; max: number; words: string };

/** The store passes rerun the same tests on the other store, so the headline count is the gate's own once. */
const COUNTED_ONCE = ['vitest', 'xunit-sqlite', 'xunit-live', 'browser-sqlite'];

const plural = (count: number, one: string) => `${count} ${one}${count === 1 ? '' : 's'}`;

/** A whole number with thousands separated, in the one locale the site writes in. */
export function figures(count: number): string {
  return count.toLocaleString('en-US');
}

/**
 * The three figures, or null while the summary has not arrived, so the strip
 * reserves its box rather than jumping when it does.
 */
export function proofFigures(summary: TestSummary | null, records: number): ProofFigure[] | null {
  if (summary === null) return null;
  const counted = summary.suites.filter((suite) => COUNTED_ONCE.includes(suite.id));
  const tests = counted.reduce((total, suite) => total + suite.passed, 0);
  const failed = summary.suites.reduce((total, suite) => total + suite.failed, 0);
  const countedFailed = counted.reduce((total, suite) => total + suite.failed, 0);
  const carried = summary.suites.filter((suite) => suite.carried);
  const stores = summary.suites.filter(
    (suite) => suite.id === 'xunit-cosmos' || suite.id === 'browser-cosmos'
  );
  return [
    {
      key: 'tests',
      figure: figures(tests),
      label: failed === 0 ? 'tests green' : `tests, ${plural(failed, 'failure')}`,
      // What the count is, and when some of it was not measured for this version, that instead:
      // a suite carried from an earlier build is the one thing a reader needs about the figure.
      detail:
        carried.length === 0
          ? 'test runs in the gate, each suite once'
          : `${plural(carried.length, 'suite')} carried from ${carried[0].carried}`,
      ring: {
        value: tests,
        max: tests + countedFailed,
        words: `${figures(tests)} of ${figures(tests + countedFailed)} tests passed`,
      },
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

/**
 * The reading inside a landing ring: the stores as a count
 * of the whole ("2/2"), the tests as a per cent ("99%"), and nothing until the
 * reading is in, so a full ring never reads as a plain circle. The per cent
 * rounds down and reads 100 only when nothing failed: 1,499 of 1,500 is 99%
 * beside its "1 failure", never 100, so a ring never claims a clean run it did not have.
 */
export function ringReading(
  key: string,
  ring: { value: number; max: number } | undefined
): string | undefined {
  if (ring === undefined || !(ring.max > 0)) return undefined;
  if (key === 'stores') return `${ring.value}/${ring.max}`;
  const share =
    ring.value >= ring.max ? 100 : Math.floor((Math.max(0, ring.value) / ring.max) * 100);
  return `${share}%`;
}
