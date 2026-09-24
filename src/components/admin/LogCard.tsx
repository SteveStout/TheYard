/**
 * The log, as the console got it (ADR-010), now or over a kept window.
 */
import { stampFor } from '../../lib/keptCards';
import styles from '../AdminPanel.module.css';
import type { LogEntry } from './types';
import { useRead, useKeptWindow, failed, About } from './common';

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
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="Recent log lines"
            tabIndex={0}
          >
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">At</th>
                  <th scope="col">Level</th>
                  <th scope="col">Category</th>
                  <th scope="col">Message</th>
                </tr>
              </thead>
              <tbody>
                {logRows.slice(0, cardWindows.logs === 'now' ? 80 : 200).map((entry, index) => (
                  <tr key={index}>
                    <td className={styles.mono}>{stampFor(cardWindows.logs, entry.at)}</td>
                    <td className={styles.mono}>{entry.level}</td>
                    <td className={styles.mono}>{entry.category}</td>
                    <td>
                      {entry.message}
                      {entry.exception === null ? null : (
                        <span className={styles.muted}> ({entry.exception})</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </article>
      {/* #endregion log-section */}
    </>
  );
}
