/**
 * Application health (ADR-010): the checks the container runs on itself, with
 * how long each took. The strip reads the same answer, so it is handed in.
 */
import styles from '../shared/card.module.css';
import type { Health, Fetched } from '../shared/types';
import { pill, failed, formatUptime } from '../shared/common';
import { keptWarmLine } from '../../../lib/keepWarm';

export default function HealthCard({ health }: { health: Fetched<Health> }) {
  return (
    <>
      {/* #region health-card */}
      <article className={`${styles.card} op-glass`} data-testid="health-card">
        <h2 className={styles.cardTitle}>Application health</h2>
        {health === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : health === 'failed' ? (
          failed('the health report')
        ) : (
          <>
            <p className={styles.statusRow}>
              <span className={pill(health.status === 'healthy')}>{health.status}</span>
              <span className={styles.muted}>
                v{health.version} · {health.commit} · up {formatUptime(health.uptime_seconds)}
              </span>
            </p>
            <ul className={styles.checkList}>
              {health.checks.map((check) => (
                <li key={check.name} className={styles.checkRow}>
                  <span className={pill(check.status === 'pass')}>{check.status}</span>
                  <span>{check.name}</span>
                  <span className={styles.muted}>{check.detail}</span>
                  <span className={styles.duration} data-testid="check-duration">
                    {check.duration_ms} ms
                  </span>
                </li>
              ))}
            </ul>
            {/* The keep-warm loop's last pass (ADR: Kept awake). */}
            <p className={styles.muted} data-testid="kept-warm">
              {keptWarmLine(health.kept_warm)}
            </p>
          </>
        )}
      </article>
      {/* #endregion health-card */}
    </>
  );
}
