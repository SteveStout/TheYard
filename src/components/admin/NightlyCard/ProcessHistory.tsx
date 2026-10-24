/**
 * One process's run history on the nightly card (ADR-109, the addendum on the six processes): a disclosure
 * closed on arrival, which asks the runs route for its first page only when it is opened (through the
 * counted request, so the working lock sees it), then the window's
 * pills, the runs newest first grouped by Central day, and "Show the next 20" with the window's totals.
 * Every word and link comes from the route; a closed history asks for nothing.
 */
import { useRef, useState } from 'react';
import {
  dedupe,
  PROCESS_WINDOWS,
  processRunsUrl,
  runsByDay,
  type CloudProcess,
  type ProcessRunRow,
  type ProcessRunsPage,
} from '../../../lib/cloudProcesses';
import { request } from '../../../lib/request';
import styles from './ProcessHistory.module.css';

/** The rows of every page read so far, one table, a day row before each Central day. */
function RunsTable({ rows, process }: { rows: ProcessRunRow[]; process: string }) {
  return (
    <div className={styles.scroll} data-testid={`process-table-${process}`}>
      <table className={styles.hist}>
        <caption className={styles.hidden}>Runs, newest first</caption>
        <thead>
          <tr>
            <th scope="col">When (Central)</th>
            <th scope="col" className={styles.n}>
              Took
            </th>
            <th scope="col">Came to</th>
            <th scope="col">It read and wrote</th>
            <th scope="col">Trace</th>
          </tr>
        </thead>
        <tbody>
          {runsByDay(rows).map((group) => [
            <tr key={`day-${group.day}`} className={styles.day}>
              <td colSpan={5}>{group.day}</td>
            </tr>,
            ...group.rows.map((row) => (
              <tr key={`${row.at_ms}-${row.trace_href ?? row.when}`} data-testid="process-run">
                <td data-label="When (Central)">{row.when}</td>
                <td data-label="Took" className={styles.n}>
                  {row.took}
                </td>
                <td data-label="Came to" className={styles.wide}>
                  <span className={styles[row.tone]}>{row.came}</span>
                </td>
                <td data-label="It read and wrote" className={styles.wide}>
                  {row.sentence}
                </td>
                <td data-label="Trace">
                  {row.trace_href === null ? (
                    ''
                  ) : (
                    <a
                      href={row.trace_href}
                      target="_blank"
                      rel="noreferrer noopener"
                      data-testid="process-trace"
                    >
                      open
                      <span className={styles.hidden}>
                        {' '}
                        the run at {row.when} in Application Insights, a new tab
                      </span>
                    </a>
                  )}
                </td>
              </tr>
            )),
          ])}
        </tbody>
      </table>
    </div>
  );
}

// #region process-history
export function ProcessHistory({ process }: { process: CloudProcess }) {
  const [window_, setWindow] = useState('7d');
  const [read, setRead] = useState<ProcessRunsPage[]>([]);
  // The windows whose last ask failed, so a failure shows beside its own window only.
  const [failedWindows, setFailedWindows] = useState<string[]>([]);
  const [open, setOpen] = useState(false);
  // The pages asked for and not yet answered, so a double click or a quick reopen asks once.
  const asking = useRef(new Set<string>());
  const failed = failedWindows.includes(window_);
  const mine = read.filter((page) => page.window === window_).sort((a, b) => a.page - b.page);
  const newest = mine[mine.length - 1];
  // A minute's runs can shift a later page by one; a run already shown is not shown twice.
  const rows = dedupe(mine.flatMap((page) => page.rows));
  /** Asks for one page through the counted request, only from an opening, a pill or "Show the next 20". */
  const ask = (window: string, page: number) => {
    const key = `${window}:${page}`;
    if (
      asking.current.has(key) ||
      read.some((held) => held.window === window && held.page === page)
    )
      return;
    asking.current.add(key);
    setFailedWindows((held) => held.filter((w) => w !== window));
    void request(processRunsUrl(process.key, window, page))
      .then((r) =>
        r.ok ? (r.json() as Promise<ProcessRunsPage>) : Promise.reject(new Error(String(r.status)))
      )
      .then((answer) =>
        setRead((held) =>
          held.some((p) => p.window === answer.window && p.page === answer.page)
            ? held
            : [...held, answer]
        )
      )
      .catch(() => setFailedWindows((held) => [...held, window]))
      .finally(() => asking.current.delete(key));
  };
  return (
    <details
      className={styles.history}
      data-testid={`process-history-${process.key}`}
      onToggle={(event) => {
        const opened = (event.currentTarget as HTMLDetailsElement).open;
        setOpen(opened);
        if (opened) ask(window_, 1);
      }}
    >
      <summary>
        Run history
        <span className={styles.count} data-testid={`process-count-${process.key}`}>
          {' · '}
          {newest !== undefined ? newest.count : process.history_count}
        </span>
      </summary>
      {open && (
        <div className={styles.body}>
          <p className={`op-seg ${styles.seg}`} role="group" aria-label={`${process.name}: window`}>
            {PROCESS_WINDOWS.map((option) => (
              <button
                key={option.key}
                type="button"
                aria-pressed={option.key === window_}
                data-testid={`process-window-${process.key}-${option.key}`}
                onClick={() => {
                  if (option.key === window_) return;
                  setWindow(option.key);
                  ask(option.key, 1);
                }}
              >
                {option.label}
              </button>
            ))}
          </p>
          {newest === undefined && !failed && <p className={styles.quiet}>Reading the runs…</p>}
          {failed && <p className={styles.quiet}>Could not read the runs on this try.</p>}
          {newest !== undefined && !newest.available && (
            <p className={styles.quiet} data-testid={`process-note-${process.key}`}>
              {newest.note}
            </p>
          )}
          {rows.length > 0 && <RunsTable rows={rows} process={process.key} />}
          {newest !== undefined && newest.available && rows.length === 0 && (
            <p className={styles.quiet}>No run in this window.</p>
          )}
          {newest !== undefined && (
            <p className={styles.more} data-testid={`process-totals-${process.key}`}>
              {newest.more && (
                <button
                  type="button"
                  className={styles.link}
                  onClick={() => ask(window_, newest.page + 1)}
                >
                  Show the next 20
                </button>
              )}
              <span className={styles.quiet}>{newest.totals}</span>
            </p>
          )}
        </div>
      )}
    </details>
  );
}
// #endregion process-history
