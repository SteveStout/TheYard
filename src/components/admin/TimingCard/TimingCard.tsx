/**
 * Timing: the requests' percentiles and every path's, with both stores on the same
 * two lines (ADR: Backends, side by side, the addendum on parity).
 */
import { documentStoreLine, percentileWords, sqlLine, timingWindow } from '../../../lib/metrics';
import { millisecondsWords } from '../../../lib/statTiles';
import chartStyles from '../charts/charts.module.css';
import cardStyles from '../shared/card.module.css';
import type { EndpointTiming, Metrics } from '../shared/types';
import { useRead, failed } from '../shared/common';
import { type Column, DataTable } from '../DataTable';

/**
 * A path: how often it was asked for, and how long it took at the middle, the
 * ninety-fifth and the worst. The ring keeps whole milliseconds, so a zero is
 * an answer under a millisecond and is written that way.
 */
const TIMING_COLUMNS: Column<EndpointTiming>[] = [
  { name: 'Path', mono: true, cell: (timing) => timing.path },
  { name: 'Calls', num: true, cell: (timing) => timing.count },
  { name: 'p50', num: true, cell: (timing) => millisecondsWords(timing.p50_ms) },
  { name: 'p95', num: true, cell: (timing) => millisecondsWords(timing.p95_ms) },
  { name: 'Slowest', num: true, cell: (timing) => millisecondsWords(timing.max_ms) },
];

export default function TimingCard({ tick }: { tick: number }) {
  const metrics = useRead<Metrics>('/api/admin/metrics', tick);
  return (
    <>
      {/* #region timing-section */}
      <article className={`${cardStyles.wide} op-glass`} data-testid="timing-card">
        <h2 className={cardStyles.cardTitle}>Timing</h2>
        {metrics === null ? (
          <p className={cardStyles.muted}>Loading…</p>
        ) : metrics === 'failed' ? (
          failed('the timing')
        ) : (
          <>
            <p className={cardStyles.muted}>{timingWindow(metrics)}</p>
            <ul className={chartStyles.summaryList}>
              <li>
                Requests: {percentileWords(metrics.requests.p50_ms, metrics.requests.p95_ms)}.
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
