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
type TelemetrySummary = {
  total: number;
  failed: number;
  p50_ms: number | null;
  p95_ms: number | null;
};
type TelemetryRoute = { name: string; calls: number; avg_ms: number | null };
type TelemetryException = { type: string; method: string; count: number; last_at: string };
type TelemetryBrowser = { count: number; last_at: string };
type Telemetry = {
  configured: boolean;
  available?: boolean;
  note?: string;
  window?: string;
  summary?: TelemetrySummary;
  slowest?: TelemetryRoute[];
  exceptions?: TelemetryException[];
  browser?: TelemetryBrowser;
};
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

/** A card's data: nothing yet, the value, or the word that the last fetch failed (ADR-017). */
type Fetched<T> = T | null | 'failed';

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
  const [health, setHealth] = useState<Fetched<Health>>(null);
  const [errors, setErrors] = useState<Fetched<ErrorEntry[]>>(null);
  const [azure, setAzure] = useState<Fetched<AzureState>>(null);
  const [telemetry, setTelemetry] = useState<Fetched<Telemetry>>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const id = window.setInterval(() => setTick((t) => t + 1), REFRESH_MS);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    let live = true;
    // A failed or non-200 answer marks the card failed instead of leaving it loading forever.
    const grab = <T,>(url: string, set: (v: Fetched<T>) => void) =>
      fetch(url)
        .then((r) =>
          r.ok ? (r.json() as Promise<T>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) set(v);
        })
        .catch(() => {
          if (live) set('failed');
        });
    void grab<Health>('/api/health', setHealth);
    void grab<ErrorEntry[]>('/api/errors', setErrors);
    void grab<AzureState>('/api/admin/azure', setAzure);
    void grab<Telemetry>('/api/admin/telemetry', setTelemetry);
    return () => {
      live = false;
    };
  }, [tick]);

  const pill = (ok: boolean) =>
    ok ? `${styles.pill} ${styles.ok}` : `${styles.pill} ${styles.bad}`;
  const failed = (what: string) => (
    <p className={styles.muted} data-testid="card-failed">
      Could not read {what} on the last try; the next try is in 30 seconds.
    </p>
  );

  return (
    <section className={styles.wrap} aria-label="Admin">
      <div className={styles.head}>
        <h1 className={styles.title}>Admin</h1>
        <button type="button" className={styles.back} onClick={onBack}>
          Back to inventory
        </button>
      </div>
      <p className={styles.blurb}>
        The running system reporting on itself: application health, what Azure says about the
        container, the last hour of traffic as Application Insights recorded it, and recent errors
        from both the server and the browser. Refreshes every 30 seconds. Public on purpose; the
        reasoning is in the Best Practices menu.
      </p>
      <div className={styles.grid}>
        {/* #region health-card */}
        <article className={styles.card} data-testid="health-card">
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

        <article className={styles.card} data-testid="azure-card">
          <h2 className={styles.cardTitle}>Azure's view of the container</h2>
          {azure === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : azure === 'failed' ? (
            failed("Azure's view")
          ) : azure.available ? (
            <ul className={styles.checkList}>
              <li className={styles.checkRow}>
                <span className={pill(azure.group_state === 'Running')}>{azure.group_state}</span>
                <span>container group</span>
              </li>
              <li className={styles.checkRow}>
                <span className={pill(azure.container_state === 'Running')}>
                  {azure.container_state}
                </span>
                <span>
                  container, {azure.restart_count} restart{azure.restart_count === 1 ? '' : 's'}
                </span>
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
              The Azure view is unavailable from here ({azure.reason}). It works when this page is
              served by the container on Azure, which asks about itself with its own identity.
            </p>
          )}
        </article>

        {/* #region telemetry-card */}
        {/* Application Insights, read back through the container's own identity
            (ADR-024). Every state the reader can answer with is rendered here:
            not configured (a local run), configured but unreadable, and the
            happy path. A telemetry panel that can break the page it reports on
            would be worse than no panel. */}
        <article className={styles.card} data-testid="telemetry-card">
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

        <article className={styles.card} data-testid="errors-card">
          <h2 className={styles.cardTitle}>Recent errors</h2>
          {errors === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : errors === 'failed' ? (
            failed('the error list')
          ) : errors.length === 0 ? (
            <p className={styles.muted}>
              None recorded since the container started, from the server or the browser. The buffer
              holds the last 50 and resets on every deploy; Application Insights keeps the durable
              copy (ADR: Telemetry).
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
