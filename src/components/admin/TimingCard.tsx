/**
 * Timing: the requests' percentiles and every path's, with both stores on the same
 * two lines (ADR: Backends, side by side, the addendum on parity).
 */
import { documentStoreLine, sqlLine, timingWindow } from '../../lib/metrics';
import styles from '../AdminPanel.module.css';
import type { Metrics } from './types';
import { useRead, failed } from './common';

export default function TimingCard({ tick }: { tick: number }) {
  const metrics = useRead<Metrics>('/api/admin/metrics', tick);
  return (
    <>
      {/* #region timing-section */}
      <article className={`${styles.wide} op-glass`} data-testid="timing-card">
        <h2 className={styles.cardTitle}>Timing</h2>
        {metrics === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : metrics === 'failed' ? (
          failed('the timing')
        ) : (
          <>
            <p className={styles.muted}>{timingWindow(metrics)}</p>
            <ul className={styles.summaryList}>
              <li>
                Requests: p50 {metrics.requests.p50_ms} ms, p95 {metrics.requests.p95_ms} ms.
              </li>
              {/* The two stores on the same two lines, whichever one serves this
                        visit (ADR: Backends, side by side, the addendum on parity). */}
              <li data-testid="timing-sql">{sqlLine(metrics)}</li>
              <li data-testid="timing-store">{documentStoreLine(metrics)}</li>
              <li>
                Answers:{' '}
                {metrics.by_status.length === 0
                  ? 'nothing recorded yet'
                  : metrics.by_status
                      .map((entry) => `${entry.count} with status ${entry.status}`)
                      .join(', ')}
                .
              </li>
            </ul>
            <div
              className={styles.tableWrap}
              role="region"
              aria-label="Request timing by endpoint"
              tabIndex={0}
            >
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Path</th>
                    <th scope="col">Calls</th>
                    <th scope="col">p50</th>
                    <th scope="col">p95</th>
                    <th scope="col">Slowest</th>
                  </tr>
                </thead>
                <tbody>
                  {metrics.requests.by_path.slice(0, 15).map((timing) => (
                    <tr key={timing.path}>
                      <td className={styles.mono}>{timing.path}</td>
                      <td>{timing.count}</td>
                      <td>{timing.p50_ms} ms</td>
                      <td>{timing.p95_ms} ms</td>
                      <td>{timing.max_ms} ms</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </article>
      {/* #endregion timing-section */}
    </>
  );
}
