/**
 * Every test, for this build (ADR: The five-minute gate).
 */
import { Fragment, useEffect, useState } from 'react';
import {
  duration,
  outcomeWord,
  rowsOf,
  type TestOrder,
  type TestResults,
  type TestRow,
  type TestSuite,
  totals,
} from '../../lib/testResults';
import styles from '../AdminPanel.module.css';
import type { Fetched } from './types';
import { About } from './common';
import { type Column, DataTable } from './DataTable';

/** A suite: its name and mark, then what passed, failed and was skipped, and how long it took. */
const SUITE_COLUMNS: Column<TestSuite>[] = [
  {
    name: 'Suite',
    rowHeader: true,
    cell: (suite) => (
      <>
        <ResultMark passed={suite.failed === 0} label /> {suite.name}
        {suite.carried && (
          <span className={styles.muted} data-testid={`tests-carried-${suite.id}`}>
            {' '}
            (carried from {suite.carried}: nothing this pass covers changed)
          </span>
        )}
      </>
    ),
  },
  { name: 'Passed', mono: true, num: true, cell: (suite) => suite.passed.toLocaleString() },
  { name: 'Failed', mono: true, num: true, cell: (suite) => suite.failed },
  { name: 'Skipped', mono: true, num: true, cell: (suite) => suite.skipped },
  { name: 'Took', mono: true, num: true, cell: (suite) => `${suite.seconds} s` },
];

/** A test: its group, its name, what it did, and how long it took. */
const TEST_COLUMNS: Column<TestRow>[] = [
  { name: 'Group', mono: true, cell: (row) => row[0] },
  { name: 'Test', cell: (row) => row[1] },
  {
    name: 'Result',
    cell: (row) => (
      <>
        {row[2] === 's' ? null : <ResultMark passed={row[2] === 'p'} />} {outcomeWord(row[2])}
      </>
    ),
  },
  { name: 'Took', mono: true, num: true, cell: (row) => duration(row[3]) },
];

/**
 * Every test the ship's gate ran for the build in the footer (ADR: The
 * five-minute gate, the addendum on every check running once). The gate runs
 * each suite once and ships what every test did; this reads it. A suite opens
 * to its tests, failures first and then the slowest, which is the list the
 * next speed-up is chosen from.
 */
export default function TestsCard() {
  const [results, setResults] = useState<Fetched<TestResults | 'none'>>(null);
  const [filter, setFilter] = useState('');
  const [order, setOrder] = useState<TestOrder>('slowest');
  // A suite's rows are drawn only while it is open: a closed list of a thousand rows is a
  // thousand rows the page and its accessibility scan would read for nothing.
  const [opened, setOpened] = useState<Record<string, boolean>>({});

  useEffect(() => {
    let live = true;
    const read = async (): Promise<TestResults | 'none'> => {
      const r = await fetch('/api/admin/tests');
      if (r.status === 404) return 'none';
      if (!r.ok) throw new Error(String(r.status));
      return (await r.json()) as TestResults;
    };
    read()
      .then((v) => {
        if (live) setResults(v);
      })
      .catch(() => {
        if (live) setResults('failed');
      });
    return () => {
      live = false;
    };
  }, []);

  return (
    <article className={`${styles.wide} op-glass`} data-testid="tests-card">
      <h2 className={styles.cardTitle}>Every test, for this build</h2>
      <About>
        The ship&rsquo;s gate runs every suite once, on the machine that ships: xUnit on SQLite and
        again booted on Cosmos DB, the live Cosmos DB tests, Vitest, and the browser suite on both
        stores, with prettier, lint, TypeScript, dotnet format and the SQL project beside them.
        Nothing is committed unless all of it passes, so nothing reaches this site untested, and
        what every test did ships with the version and is read here. Open a suite for its tests,
        failures first and then the slowest.
      </About>
      {results === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : results === 'failed' ? (
        <p className={styles.muted} data-testid="tests-failed">
          Could not read the test results on the last try.
        </p>
      ) : results === 'none' ? (
        <p className={styles.muted} data-testid="tests-none">
          No test results shipped with this build. The ship&rsquo;s gate writes them.
        </p>
      ) : (
        <>
          <TestsSummary results={results} />
          <p className={styles.muted} data-testid="tests-checks">
            {results.checks.map((check, index) => (
              <Fragment key={check.name}>
                {index > 0 ? ' · ' : ''}
                <span className={styles.checkItem}>
                  <ResultMark passed={check.passed} />
                  {` ${check.name} ${check.passed ? 'passed' : 'failed'} in ${check.seconds} s`}
                </span>
              </Fragment>
            ))}
          </p>
          <DataTable
            label="Every suite"
            testId="tests-suites"
            rows={results.suites}
            rowKey={(suite) => suite.id}
            rowProps={(suite) => ({ testId: `tests-suite-${suite.id}` })}
            columns={SUITE_COLUMNS}
          />
          <form
            className={styles.filterRow}
            aria-label="Find a test"
            onSubmit={(event) => event.preventDefault()}
          >
            <label>
              Test name contains{' '}
              <input
                placeholder="any"
                value={filter}
                onChange={(event) => setFilter(event.target.value)}
                data-testid="tests-filter"
              />
            </label>
            <button
              type="button"
              className={styles.back}
              aria-pressed={order === 'name'}
              onClick={() => setOrder((o) => (o === 'slowest' ? 'name' : 'slowest'))}
              data-testid="tests-order"
            >
              {order === 'slowest' ? 'Sort by name' : 'Sort slowest first'}
            </button>
          </form>
          {results.suites.map((suite) => {
            const rows = rowsOf(suite, filter, order);
            const open = opened[suite.id] ?? suite.failed > 0;
            return (
              <details
                key={suite.id}
                data-testid={`tests-list-${suite.id}`}
                open={open}
                onToggle={(event) => {
                  const now = event.currentTarget.open;
                  setOpened((was) => ({ ...was, [suite.id]: now }));
                }}
              >
                <summary>
                  <ResultMark passed={suite.failed === 0} /> {suite.name}:{' '}
                  {rows.length.toLocaleString()} of {suite.tests.length.toLocaleString()} tests
                </summary>
                {open && rows.length > 0 && (
                  <DataTable
                    label={`${suite.name}, every test`}
                    rows={rows}
                    rowKey={(_row, index) => index}
                    columns={TEST_COLUMNS}
                  />
                )}
              </details>
            );
          })}
        </>
      )}
    </article>
  );
}

/** The card's one sentence: how many passed, of how many, for which build, and how long the gate took. */
function TestsSummary({ results }: { results: TestResults }) {
  const sums = totals(results);
  const extra = [
    sums.failed > 0 ? `${sums.failed} failed` : null,
    sums.skipped > 0 ? `${sums.skipped} skipped` : null,
  ].filter(Boolean);
  return (
    <div className={styles.testsHeadline}>
      <ResultMark passed={sums.failed === 0} big />
      <p data-testid="tests-summary">
        <strong>
          {sums.passed.toLocaleString()} of {sums.tests.toLocaleString()} tests passed
        </strong>
        {extra.length > 0 ? `, ${extra.join(', ')}` : ''}, for {results.version} in the ship&rsquo;s
        gate at {new Date(results.ranAt).toLocaleString()}, {results.gateSeconds} s from the first
        check to the last.
      </p>
    </div>
  );
}

/**
 * Passed or failed, as a mark nobody has to read: a tick in a green disc or a cross in a
 * red one, from the status tokens, big beside the card's sentence and small on every suite,
 * check and test. It carries its own label only where no word beside it says the same.
 */
function ResultMark({
  passed,
  big = false,
  label = false,
}: {
  passed: boolean;
  big?: boolean;
  label?: boolean;
}) {
  const size = big ? 44 : 16;
  return (
    <svg
      className={`${styles.resultMark} ${passed ? styles.resultPass : styles.resultFail}`}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      data-verdict={passed ? 'pass' : 'fail'}
      {...(label
        ? { role: 'img', 'aria-label': passed ? 'passed' : 'failed' }
        : { 'aria-hidden': true })}
    >
      <circle cx="12" cy="12" r="10.5" className={styles.resultDisc} />
      <path d={passed ? 'M7 12.5l3.2 3.2L17 8.8' : 'M8.5 8.5l7 7M15.5 8.5l-7 7'} />
    </svg>
  );
}
