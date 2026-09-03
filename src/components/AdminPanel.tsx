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
type SqlParameterShape = { name: string; type: string; size: number | null };
type SqlStatement = {
  at: string;
  text: string;
  parameters: SqlParameterShape[];
  duration_ms: number;
  outcome: string;
  request: string | null;
};
type LogEntry = {
  at: string;
  level: string;
  category: string;
  message: string;
  exception: string | null;
};
type EndpointTiming = {
  path: string;
  count: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
};
type Metrics = {
  requests: { window: number; p50_ms: number; p95_ms: number; by_path: EndpointTiming[] };
  sql: { window: number; p50_ms: number; p95_ms: number; max_ms: number };
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
  const [sql, setSql] = useState<Fetched<SqlStatement[]>>(null);
  const [logs, setLogs] = useState<Fetched<LogEntry[]>>(null);
  const [metrics, setMetrics] = useState<Fetched<Metrics>>(null);
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
    void grab<SqlStatement[]>('/api/admin/sql', setSql);
    void grab<LogEntry[]>('/api/admin/logs', setLogs);
    void grab<Metrics>('/api/admin/metrics', setMetrics);
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
        container, the last hour of traffic as Application Insights recorded it, recent errors from
        both the server and the browser, and below those, every SQL statement it has sent, its own
        log, and how long both take. Refreshes every 30 seconds. Public on purpose; the reasoning is
        in the Best Practices menu.
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

      {/* #region timing-section */}
      <article className={styles.wide} data-testid="timing-card">
        <h2 className={styles.cardTitle}>Timing</h2>
        {metrics === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : metrics === 'failed' ? (
          failed('the timing')
        ) : (
          <>
            <p className={styles.muted}>
              Measured in this process, over the last {metrics.requests.window} requests and{' '}
              {metrics.sql.window} statements. Requests p50 {metrics.requests.p50_ms} ms, p95{' '}
              {metrics.requests.p95_ms} ms. SQL p50 {metrics.sql.p50_ms} ms, p95{' '}
              {metrics.sql.p95_ms} ms, slowest {metrics.sql.max_ms} ms.
            </p>
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

      {/* #region sql-section */}
      <article className={styles.wide} data-testid="sql-card">
        <h2 className={styles.cardTitle}>The SQL this application ran</h2>
        <p className={styles.muted}>
          Every statement Entity Framework sent, newest first, with the request that caused it and
          how long the database took. Parameters are listed by name, type and size. Their values are
          not here and never were: the type this table is built from has no field to put one in,
          because this page is public and a registration&rsquo;s parameters carry an email address.
          The buffer holds the last 200 statements in this container&rsquo;s memory and empties on
          every deploy.
        </p>
        {sql === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : sql === 'failed' ? (
          failed('the SQL log')
        ) : sql.length === 0 ? (
          <p className={styles.muted}>
            Nothing recorded yet. The catalogue is read once at startup and cached, so an idle
            container runs no SQL at all.
          </p>
        ) : (
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="SQL statements this application ran"
            tabIndex={0}
          >
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">At</th>
                  <th scope="col">Took</th>
                  <th scope="col">Caused by</th>
                  <th scope="col">Statement</th>
                  <th scope="col">Parameters</th>
                </tr>
              </thead>
              <tbody>
                {sql.slice(0, 60).map((statement, index) => (
                  <tr key={index}>
                    <td className={styles.mono}>{new Date(statement.at).toLocaleTimeString()}</td>
                    <td className={styles.mono}>{statement.duration_ms} ms</td>
                    <td className={styles.mono}>{statement.request ?? 'startup'}</td>
                    <td>
                      <pre className={styles.sql}>{statement.text}</pre>
                      <span className={styles.muted}>{statement.outcome}</span>
                    </td>
                    <td className={styles.mono}>
                      {statement.parameters.length === 0
                        ? 'none'
                        : statement.parameters
                            .map(
                              (parameter) =>
                                `${parameter.name} ${parameter.type}` +
                                (parameter.size === null ? '' : `(${parameter.size})`)
                            )
                            .join(', ')}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </article>
      {/* #endregion sql-section */}

      {/* #region log-section */}
      <article className={styles.wide} data-testid="log-card">
        <h2 className={styles.cardTitle}>The log, as the console got it</h2>
        <p className={styles.muted}>
          The application&rsquo;s own log lines at Information and above, newest first, holding the
          last 300 in memory. An exception shows its type; its message stays server-side, because a
          database driver will happily quote the value that broke a constraint.
        </p>
        {logs === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : logs === 'failed' ? (
          failed('the log')
        ) : logs.length === 0 ? (
          <p className={styles.muted}>Nothing recorded since the container started.</p>
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
                {logs.slice(0, 80).map((entry, index) => (
                  <tr key={index}>
                    <td className={styles.mono}>{new Date(entry.at).toLocaleTimeString()}</td>
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
    </section>
  );
}
