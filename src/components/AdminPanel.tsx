import { useEffect, useState } from 'react';
import styles from './AdminPanel.module.css';

type HealthCheck = { name: string; status: string; detail: string; duration_ms: number };
type Health = {
  status: string;
  uptime_seconds: number;
  version: string;
  commit: string;
  checks: HealthCheck[];
};
type ErrorEntry = { at: string; path: string; status: number; message: string };
type AzureEvent = { name: string; count: number; last_at: string; message: string };
type AzureState = {
  available: boolean;
  reason?: string;
  group_state?: string;
  container_state?: string;
  restart_count?: number;
  image?: string;
  events?: AzureEvent[];
  fetched_at?: string;
};

const REFRESH_MS = 30_000;

function formatUptime(totalSeconds: number): string {
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h ${minutes}m`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m ${Math.floor(totalSeconds % 60)}s`;
}

/**
 * The Admin tab (ADR-010): the running system reporting on itself. Three
 * cards fetch independently and degrade independently, so a dead Azure
 * leg never hides app health. Public on purpose; the ADR explains why.
 */
export function AdminPanel({ onBack }: { onBack: () => void }) {
  const [health, setHealth] = useState<Health | null>(null);
  const [errors, setErrors] = useState<ErrorEntry[] | null>(null);
  const [azure, setAzure] = useState<AzureState | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const id = window.setInterval(() => setTick((t) => t + 1), REFRESH_MS);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    let live = true;
    const grab = <T,>(url: string, set: (v: T | null) => void) =>
      fetch(url)
        .then((r) => (r.ok ? r.json() : null))
        .then((v) => { if (live) set(v); })
        .catch(() => { if (live) set(null); });
    void grab<Health>('/api/health', setHealth);
    void grab<ErrorEntry[]>('/api/errors', setErrors);
    void grab<AzureState>('/api/admin/azure', setAzure);
    return () => { live = false; };
  }, [tick]);

  const pill = (ok: boolean) => (ok ? `${styles.pill} ${styles.ok}` : `${styles.pill} ${styles.bad}`);

  return (
    <section className={styles.wrap} aria-label="Admin">
      <div className={styles.head}>
        <h1 className={styles.title}>Admin</h1>
        <button type="button" className={styles.back} onClick={onBack}>
          Back to inventory
        </button>
      </div>
      <p className={styles.blurb}>
        The running system reporting on itself: application health, what Azure
        says about the container, and recent server errors. Refreshes every 30
        seconds. Public on purpose; the reasoning is in the Best Practices menu.
      </p>
      <div className={styles.grid}>
        <article className={styles.card} data-testid="health-card">
          <h2 className={styles.cardTitle}>Application health</h2>
          {health === null ? (
            <p className={styles.muted}>Loading…</p>
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

        <article className={styles.card} data-testid="azure-card">
          <h2 className={styles.cardTitle}>Azure's view of the container</h2>
          {azure === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : azure.available ? (
            <ul className={styles.checkList}>
              <li className={styles.checkRow}>
                <span className={pill(azure.group_state === 'Running')}>{azure.group_state}</span>
                <span>container group</span>
              </li>
              <li className={styles.checkRow}>
                <span className={pill(azure.container_state === 'Running')}>{azure.container_state}</span>
                <span>container, {azure.restart_count} restart{azure.restart_count === 1 ? '' : 's'}</span>
              </li>
              <li className={styles.checkRow}>
                <span className={styles.mono}>{azure.image?.split('/').pop()}</span>
                <span className={styles.muted}>image Azure reports</span>
              </li>
              {azure.events && azure.events.length > 0 && (
                <li className={styles.checkRow}>
                  <span className={styles.muted}>recent events, newest first</span>
                </li>
              )}
              {azure.events?.map((event, index) => (
                <li key={index} className={styles.checkRow} data-testid="azure-event">
                  <span className={styles.mono}>{event.name}</span>
                  <span className={styles.muted}>
                    {event.count > 1 ? `${event.count} times, last ` : ''}
                    {event.last_at ? new Date(event.last_at).toLocaleString() : ''}
                  </span>
                  <span className={styles.muted}>{event.message}</span>
                </li>
              ))}
            </ul>
          ) : (
            <p className={styles.muted}>
              The Azure view is unavailable from here ({azure.reason}). It works
              when this page is served by the container on Azure, which asks
              about itself with its own identity.
            </p>
          )}
        </article>

        <article className={styles.card} data-testid="errors-card">
          <h2 className={styles.cardTitle}>Recent server errors</h2>
          {errors === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : errors.length === 0 ? (
            <p className={styles.muted}>
              None recorded since the container started. The buffer holds the
              last 50 and resets on every deploy.
            </p>
          ) : (
            <ul className={styles.errorList}>
              {errors.map((entry, index) => (
                <li key={index} className={styles.errorRow}>
                  <span className={styles.mono}>{new Date(entry.at).toLocaleTimeString()}</span>
                  <span className={styles.mono}>{entry.status}</span>
                  <span className={styles.mono}>{entry.path}</span>
                  <span className={styles.muted}>{entry.message}</span>
                </li>
              ))}
            </ul>
          )}
        </article>
      </div>
    </section>
  );
}
