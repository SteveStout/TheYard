/**
 * Recent errors, from the server and the browser (ADR-023), now or over a kept
 * window. The strip reads the ring for its tile, so the ring is handed in.
 */
import { stampFor } from '../../lib/keptCards';
import styles from '../AdminPanel.module.css';
import type { ErrorEntry, Fetched } from './types';
import { useKeptWindow, failed } from './common';

export default function ErrorsCard({
  errors,
  tick,
}: {
  errors: Fetched<ErrorEntry[]>;
  tick: number;
}) {
  const kept = useKeptWindow<ErrorEntry>('errors', tick);
  const cardWindows = { errors: kept.window };
  const picker = (_card: string) => kept.picker;
  const errorRows = kept.window === 'now' ? errors : kept.rows;
  return (
    <article className={`${styles.card} ${styles.wideCard} op-glass`} data-testid="errors-card">
      <h2 className={styles.cardTitle}>Recent errors</h2>
      {picker('errors')}
      {errorRows === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : errorRows === 'failed' ? (
        failed('the error list')
      ) : errorRows.length === 0 ? (
        cardWindows.errors !== 'now' ? null : (
          <p className={styles.muted}>
            None recorded since the container started, from the server or the browser. The buffer
            holds the last 50 and resets on every deploy; Application Insights keeps the durable
            copy (ADR: Telemetry). A server error carries its stack, file and line beside it; the
            exception&rsquo;s message is deliberately not here, because a message is where a
            framework writes a connection detail and this page is public.
          </p>
        )
      ) : (
        <div className={styles.tableWrap} role="region" aria-label="Recent errors" tabIndex={0}>
          <table className={styles.table} data-testid="errors-table">
            <thead>
              <tr>
                <th scope="col">At</th>
                <th scope="col">Status</th>
                <th scope="col">Where</th>
                <th scope="col">What</th>
                <th scope="col">Stack</th>
              </tr>
            </thead>
            <tbody>
              {errorRows.map((entry, index) => (
                <tr key={index}>
                  <td className={styles.mono}>{stampFor(cardWindows.errors, entry.at)}</td>
                  <td className={styles.mono}>{entry.status === 0 ? 'browser' : entry.status}</td>
                  <td className={styles.mono}>{entry.path}</td>
                  <td>{entry.message}</td>
                  <td>
                    {entry.frames.length === 0 ? (
                      <span className={styles.muted}>no stack</span>
                    ) : (
                      <details data-testid={`error-frames-${index}`}>
                        <summary>{entry.frames.length} frames</summary>
                        <pre className={styles.sql}>{entry.frames.join('\n')}</pre>
                      </details>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </article>
  );
}
