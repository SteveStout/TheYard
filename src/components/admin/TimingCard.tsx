/**
 * Timing: the requests' percentiles and every path's, with both stores on the same
 * two lines (ADR: Backends, side by side, the addendum on parity).
 */
import { documentStoreLine, sqlLine, timingWindow } from '../../lib/metrics';
import styles from '../AdminPanel.module.css';
import type { EndpointTiming, Metrics } from './types';
import { useRead, failed } from './common';
import { type Column, DataTable } from './DataTable';

/** A path: how often it was asked for, and how long it took at the middle, the ninety-fifth and the worst. */
const TIMING_COLUMNS: Column<EndpointTiming>[] = [
  { name: 'Path', mono: true, cell: (timing) => timing.path },
  { name: 'Calls', num: true, cell: (timing) => timing.count },
  { name: 'p50', num: true, cell: (timing) => `${timing.p50_ms} ms` },
  { name: 'p95', num: true, cell: (timing) => `${timing.p95_ms} ms` },
  { name: 'Slowest', num: true, cell: (timing) => `${timing.max_ms} ms` },
];

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
            <DataTable
              label="Request timing by endpoint"
              rows={metrics.requests.by_path.slice(0, 15)}
              rowKey={(timing) => timing.path}
              columns={TIMING_COLUMNS}
            />
          </>
        )}
      </article>
      {/* #endregion timing-section */}
    </>
  );
}
