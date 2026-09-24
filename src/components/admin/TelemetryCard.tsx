/**
 * Application Insights, read back through the container's own identity (ADR-024).
 */
import styles from '../AdminPanel.module.css';
import type { Telemetry } from './types';
import { useRead, pill, failed } from './common';

export default function TelemetryCard({ tick }: { tick: number }) {
  const telemetry = useRead<Telemetry>('/api/admin/telemetry', tick);
  return (
    <>
      {/* #region telemetry-card */}
      {/* Application Insights, read back through the container's own identity
                  (ADR-024). Every state the reader can answer with is rendered here:
                  not configured (a local run), configured but unreadable, and the
                  happy path. A telemetry panel that can break the page it reports on
                  would be worse than no panel. */}
      <article className={`${styles.card} op-glass`} data-testid="telemetry-card">
        <h2 className={styles.cardTitle}>Traffic, last hour</h2>
        {telemetry === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : telemetry === 'failed' ? (
          failed('the telemetry')
        ) : !telemetry.configured || telemetry.available === false ? (
          <p className={styles.muted} data-testid="telemetry-note">
            {telemetry.note}
          </p>
        ) : (
          <>
            <div className={styles.statusRow}>
              <span className={pill((telemetry.summary?.failed ?? 0) === 0)}>
                {telemetry.summary?.total ?? 0} request
                {(telemetry.summary?.total ?? 0) === 1 ? '' : 's'}
              </span>
              <span className={styles.muted}>{telemetry.summary?.failed ?? 0} failed</span>
              <span className={styles.mono}>p50 {telemetry.summary?.p50_ms ?? 0} ms</span>
              <span className={styles.mono}>p95 {telemetry.summary?.p95_ms ?? 0} ms</span>
              {/* Steve asked for every React error, so the count of them is
                          on the card rather than only in the portal. */}
              <span className={pill((telemetry.browser?.count ?? 0) === 0)}>
                {telemetry.browser?.count ?? 0} browser
              </span>
            </div>
            {telemetry.slowest && telemetry.slowest.length > 0 && (
              <p className={styles.muted}>Slowest routes</p>
            )}
            <ul className={styles.checkList}>
              {telemetry.slowest?.map((route) => (
                <li key={route.name} className={styles.checkRow} data-testid="telemetry-route">
                  <span className={styles.mono}>{route.name}</span>
                  <span className={styles.duration}>{route.avg_ms} ms</span>
                  <span className={styles.muted}>
                    {route.calls} call{route.calls === 1 ? '' : 's'}
                  </span>
                </li>
              ))}
            </ul>
            {telemetry.exceptions && telemetry.exceptions.length > 0 && (
              <>
                <p className={styles.muted}>Exceptions</p>
                <ul className={styles.errorList}>
                  {telemetry.exceptions.map((entry, index) => (
                    <li key={index} className={styles.errorRow} data-testid="telemetry-exception">
                      <span className={styles.mono}>{entry.type}</span>
                      <span className={styles.muted}>{entry.method}</span>
                      <span className={styles.muted}>
                        {entry.count} time{entry.count === 1 ? '' : 's'}
                      </span>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </>
        )}
      </article>
      {/* #endregion telemetry-card */}
    </>
  );
}
