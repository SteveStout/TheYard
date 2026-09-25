/**
 * The log, as the console got it (ADR-010), now or over a kept window.
 */
import { type CardWindow, stampFor } from '../../lib/keptCards';
import styles from '../AdminPanel.module.css';
import type { LogEntry } from './types';
import { useRead, useKeptWindow, failed, About } from './common';
import { type Column, DataTable } from './DataTable';

/** A log line: when, how loud, from where, and what it said. */
const logColumns = (window_: CardWindow): Column<LogEntry>[] => [
  { name: 'At', mono: true, cell: (entry) => stampFor(window_, entry.at) },
  { name: 'Level', mono: true, cell: (entry) => entry.level },
  { name: 'Category', mono: true, cell: (entry) => entry.category },
  {
    name: 'Message',
    cell: (entry) => (
      <>
        {entry.message}
        {entry.exception === null ? null : (
          <span className={styles.muted}> ({entry.exception})</span>
        )}
      </>
    ),
  },
];

export default function LogCard({ tick }: { tick: number }) {
  const logs = useRead<LogEntry[]>('/api/admin/logs', tick);
  const kept = useKeptWindow<LogEntry>('logs', tick);
  const cardWindows = { logs: kept.window };
  const picker = (_card: string) => kept.picker;
  const logRows = kept.window === 'now' ? logs : kept.rows;
  return (
    <>
      {/* #region log-section */}
      <article className={`${styles.wide} op-glass`} data-testid="log-card">
        <h2 className={styles.cardTitle}>The log, as the console got it</h2>
        <About>
          This application&rsquo;s own log lines at Information and above, newest first, holding the
          last 300 in memory. Its own, which since 1.0.0.103 includes one line per document store
          operation with its charge and its time, and the one framework category that gives every
          SQL statement a line of its own: the rest of the framework is left out because a healthy
          container announces its content root and its key directory, and those are server paths on
          a public page. An exception shows its type. Its message stays server-side, because a
          database driver writes the server name, the login name and the caller&rsquo;s address into
          one.
        </About>
        {picker('logs')}
        {logRows === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : logRows === 'failed' ? (
          failed('the log')
        ) : logRows.length === 0 ? (
          cardWindows.logs !== 'now' ? null : (
            <p className={styles.muted}>Nothing recorded since the container started.</p>
          )
        ) : (
          <DataTable
            label="Recent log lines"
            rows={logRows.slice(0, cardWindows.logs === 'now' ? 80 : 200)}
            rowKey={(_entry, index) => index}
            columns={logColumns(cardWindows.logs)}
          />
        )}
      </article>
      {/* #endregion log-section */}
    </>
  );
}
