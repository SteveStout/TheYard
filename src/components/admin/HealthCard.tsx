/**
 * Application health (ADR-010): the checks the container runs on itself, with
 * how long each took. The strip reads the same answer, so it is handed in.
 */
import styles from '../AdminPanel.module.css';
import type { Health, Fetched } from './types';
import { pill, failed, formatUptime } from './common';

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
          </>
        )}
      </article>
      {/* #endregion health-card */}
    </>
  );
}
