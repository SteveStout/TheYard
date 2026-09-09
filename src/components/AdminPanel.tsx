import { useEffect, useState } from 'react';
import { shortenDigests } from '../lib/format';
import { documentStore, documentStoreLine, sqlLine, timingWindow } from '../lib/metrics';
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
type StatusCount = { status: number; count: number };
type RouteTiming = { route: string; count: number; p50_ms: number; p95_ms: number; max_ms: number };
type StoreSummary = {
  store: string;
  window: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
  ru_total: number;
  ru_p50: number;
  ru_max: number;
  cross_partition: number;
  point_operations: number;
};
type RouteCharge = {
  route: string;
  requests: number;
  operations_per_request: number;
  ru_p50: number;
  ru_max: number;
  cross_partition: number;
};
type Startup = {
  store: string;
  prepare_ms: number | null;
  schema_ms: number;
  seed_ms: number;
  seed_ru: number | null;
  catalogue_ms: number | null;
  bids_ms: number | null;
  ready_ms: number | null;
  started_at: string;
};
type SqlSummary = { window: number; p50_ms: number; p95_ms: number; max_ms: number };
/** One store this container runs, on the rows the comparison card draws (ADR: One container, both stores). */
type BackendMetrics = {
  key: string;
  store: string;
  ready: boolean;
  default: boolean;
  startup: Startup;
  requests: { window: number; p50_ms: number; p95_ms: number; by_route: RouteTiming[] };
  store_metrics: StoreSummary | null;
  store_by_route: RouteCharge[];
  sql: SqlSummary | null;
};
type Metrics = {
  requests: {
    window: number;
    p50_ms: number;
    p95_ms: number;
    by_path: EndpointTiming[];
    by_route: RouteTiming[];
  };
  by_status: StatusCount[];
  sql: SqlSummary;
  store: StoreSummary;
  store_by_route: RouteCharge[];
  startup: Startup;
  /** Absent from a peer on an older build, so read as optional. */
  backends?: BackendMetrics[];
};
type Peer = {
  configured: boolean;
  reachable: boolean;
  reason: string | null;
  host: string | null;
  fetched_at: string;
  metrics: Metrics | null;
};
type StoreOperation = {
  at: string;
  container: string;
  kind: string;
  text: string;
  parameters: SqlParameterShape[];
  partition: string;
  physical_partitions: number;
  request_charge: number;
  duration_ms: number;
  outcome: string;
  request: string | null;
};
type StoreLog = { store: string; operations: StoreOperation[] };
type ExperimentRow = {
  query: string;
  partitions: string;
  request_charge: number;
  duration_ms: number;
  documents: number;
};
type Experiment = {
  available: boolean;
  reason: string | null;
  container?: string;
  physical_partitions?: number;
  documents?: number;
  rows: ExperimentRow[];
  ran_at?: string;
};

/** The rows the comparison card puts side by side: the paths a visitor actually takes (ADR: Backends, side by side). */
const COMPARED_ROUTES: { route: string; label: string }[] = [
  { route: 'GET /api/vehicles', label: 'Listing page' },
  { route: 'GET /api/vehicles/{id}', label: 'Vehicle page' },
  { route: 'GET /api/facets', label: 'Filter values' },
  { route: 'POST /api/vehicles/{id}/bids', label: 'Bid write' },
  { route: 'POST /api/auth/login', label: 'Sign in' },
  { route: 'POST /api/auth/register', label: 'Register' },
  { route: 'POST /api/market/tick', label: 'Room tick' },
];
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
  const [peer, setPeer] = useState<Fetched<Peer>>(null);
  const [store, setStore] = useState<Fetched<StoreLog>>(null);
  const [experiment, setExperiment] = useState<Fetched<Experiment>>(null);
  const [proof, setProof] = useState<Fetched<Proof>>(null);
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
    void grab<Peer>('/api/admin/peer', setPeer);
    void grab<StoreLog>('/api/admin/store', setStore);
    void grab<Experiment>('/api/admin/experiment', setExperiment);
    void grab<Proof>('/api/admin/proof', setProof);
    return () => {
      live = false;
    };
  }, [tick]);

  // #region run-proof
  // Start a run, then read the card every three seconds until it is no longer
  // running, because the thirty-second refresh above would leave the button
  // saying "Running" long after the result had landed.
  const runProof = async () => {
    const started = await fetch('/api/admin/proof', { method: 'POST' }).catch(() => null);
    if (started === null || (!started.ok && started.status !== 409)) {
      setProof('failed');
      return;
    }
    setProof((prior) => ({
      status: 'running',
      result: prior !== null && prior !== 'failed' ? prior.result : null,
    }));
    const poll = window.setInterval(() => {
      void fetch('/api/admin/proof')
        .then((r) =>
          r.ok ? (r.json() as Promise<Proof>) : Promise.reject(new Error(String(r.status)))
        )
        .then((latest) => {
          if (latest.status !== 'running') {
            window.clearInterval(poll);
            setProof(latest);
          }
        })
        .catch(() => {
          window.clearInterval(poll);
          setProof('failed');
        });
    }, 3000);
  };
  // #endregion run-proof

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
        The running system reporting on itself: the two stores side by side, application health,
        what Azure says about the container, the last hour of traffic as Application Insights
        recorded it, recent errors from both the server and the browser, and below those, every SQL
        statement and every document store operation it has sent, its own log, and how long each
        takes. Every statistic the page shows for one store it shows for the other, and where a
        number has no meaning on one side the page says so in words. Refreshes every 30 seconds.
        Public on purpose; the reasoning is in the Best Practices menu.
      </p>
      {/* #region backends-card */}
      <article className={styles.wide} data-testid="backends-card">
        <h2 className={styles.cardTitle}>Backends, side by side</h2>
        <p className={styles.muted}>
          The two stores on the same rows: how long each took to come up, what the seed cost, and
          how long the things a visitor does take on each, with the request charge beside every
          number the document store can put one on. A container that runs both stores compares them
          with each other, in one process, the one serving this visit first; a container that runs
          one compares itself with its peer, read through its own API with two and a half seconds of
          patience, so a peer that is down is a sentence here and not a hang. Cold start and seed
          are measured on each store at its own start; the rest is the last few hundred requests
          each has served.
        </p>
        {metrics === null || peer === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : metrics === 'failed' ? (
          failed('the comparison')
        ) : (
          <Comparison mine={metrics} peer={peer === 'failed' ? null : peer} />
        )}
      </article>
      {/* #endregion backends-card */}

      <ProofCard proof={proof} onRun={() => void runProof()} />

      {/* #region experiment-card */}
      <article className={styles.wide} data-testid="experiment-card">
        <h2 className={styles.cardTitle}>The partition key, live</h2>
        <p className={styles.muted}>
          Seven queries against a container of 100,000 vehicles partitioned on the make, run by this
          container with its own identity when this page asks, and cached for a minute. A query that
          names the make runs inside one logical partition; one that cannot fans out across every
          physical partition, and the request charge beside each is what that costs. The reasoning,
          the alternatives and the honest caveat about how many physical partitions there are at
          this size are in the partition key record.
        </p>
        {experiment === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : experiment === 'failed' ? (
          failed('the experiment')
        ) : !experiment.available ? (
          <p className={styles.muted} data-testid="experiment-note">
            Not available here: {experiment.reason}
          </p>
        ) : (
          <>
            <p className={styles.muted}>
              {experiment.container}: {experiment.documents?.toLocaleString()} documents on{' '}
              {experiment.physical_partitions} physical partition
              {experiment.physical_partitions === 1 ? '' : 's'}, measured at{' '}
              {experiment.ran_at ? new Date(experiment.ran_at).toLocaleTimeString() : ''}.
            </p>
            <div
              className={styles.tableWrap}
              role="region"
              aria-label="Queries against the partitioned catalogue"
              tabIndex={0}
            >
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Query</th>
                    <th scope="col">Partitions</th>
                    <th scope="col">Charge</th>
                    <th scope="col">Took</th>
                    <th scope="col">Documents</th>
                  </tr>
                </thead>
                <tbody>
                  {experiment.rows.map((row) => (
                    <tr key={row.query}>
                      <td>{row.query}</td>
                      <td className={styles.mono}>{row.partitions}</td>
                      <td className={styles.mono}>{row.request_charge} RU</td>
                      <td className={styles.mono}>{row.duration_ms} ms</td>
                      <td className={styles.mono}>{row.documents}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </article>
      {/* #endregion experiment-card */}

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
                  <span className={styles.muted}>{shortenDigests(event.message)}</span>
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

        <article className={`${styles.card} ${styles.wideCard}`} data-testid="errors-card">
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

      {/* #region sql-section */}
      {/* The document store's card when this container has a document store,
          the SQL card when it has a relational one, and both when it runs both
          (ADR: One container, both stores). Each card's rule reads the
          backends list first, which names every store the container runs
          whichever one serves this visit, and falls back on the log's own
          label for a container on an older build. */}
      {store !== null &&
      store !== 'failed' &&
      (store.store === 'Azure Cosmos DB' ||
        (metrics !== null && metrics !== 'failed' && documentStore(metrics) !== null)) ? (
        <StoreCard log={store} />
      ) : null}
      {store === null ||
      store === 'failed' ||
      store.store !== 'Azure Cosmos DB' ||
      (metrics !== null &&
        metrics !== 'failed' &&
        (metrics.backends ?? []).some((backend) => backend.sql !== null)) ? (
        <article className={styles.wide} data-testid="sql-card">
          <h2 className={styles.cardTitle}>The SQL this application ran</h2>
          <p className={styles.muted}>
            Every statement Entity Framework sent, newest first, with the request that caused it and
            how long the database took. Parameters are listed by name, type and size. Their values
            are not here and never were: the type this table is built from has no field to put one
            in, because this page is public and a registration&rsquo;s parameters carry an email
            address. The request is the method and the path, without its query string, for the same
            reason. Statements caused by this page and by the health check are left out, or watching
            would be all there was to see. The buffer holds the last 200 in this container&rsquo;s
            memory and empties on every deploy.
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
                      <td className={styles.mono}>{describeParameters(statement.parameters)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </article>
      ) : null}
      {/* #endregion sql-section */}

      {/* #region log-section */}
      <article className={styles.wide} data-testid="log-card">
        <h2 className={styles.cardTitle}>The log, as the console got it</h2>
        <p className={styles.muted}>
          This application&rsquo;s own log lines at Information and above, newest first, holding the
          last 300 in memory. Its own, which since 1.0.0.103 includes one line per document store
          operation with its charge and its time, and the one framework category that gives every
          SQL statement a line of its own: the rest of the framework is left out because a healthy
          container announces its content root and its key directory, and those are server paths on
          a public page. An exception shows its type. Its message stays server-side, because a
          database driver writes the server name, the login name and the caller&rsquo;s address into
          one.
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

function describeParameters(parameters: SqlParameterShape[]): string {
  return parameters.length === 0
    ? 'none'
    : parameters
        .map(
          (parameter) =>
            `${parameter.name} ${parameter.type}` +
            (parameter.size === null ? '' : `(${parameter.size})`)
        )
        .join(', ');
}

/** A number of milliseconds, or the word for not having one. */
function ms(value: number | null | undefined): string {
  return value === null || value === undefined ? 'not measured' : `${value} ms`;
}

/** Milliseconds with the request charge beside them, when there is one (ADR: Backends, side by side). */
function msAndRu(value: number | null | undefined, ru: number | null | undefined): string {
  const time = ms(value);
  return ru === null || ru === undefined ? time : `${time} · ${ru} RU`;
}

// #region proof-card
type ProofCell = {
  store: string;
  samples: number;
  p50_ms: number;
  p95_ms: number;
  operations_per_request: number;
  request_units_per_request: number | null;
  failures: number;
};
type ProofRow = {
  path: string;
  label: string;
  cells: ProofCell[];
  median_difference_ms: number | null;
  difference_without_hops_ms: number | null;
  verdict: string;
};
type ProofResult = {
  status: 'done' | 'failed';
  reason: string | null;
  started_at: string | null;
  finished_at: string | null;
  rounds: number;
  stores: { key: string; name: string; hop_ms: number | null }[];
  rows: ProofRow[];
  sentence: string;
};
type Proof = { status: 'idle' | 'running' | 'done' | 'failed'; result: ProofResult | null };

/**
 * The performance proof, run on demand and read back every refresh (ADR: Same
 * performance, proven). The button starts a run in the container; the card
 * says it is running until the result lands, then shows every path on both
 * stores with the paired difference and a verdict, and the sentence the
 * whole card adds up to.
 */
function ProofCard({ proof, onRun }: { proof: Fetched<Proof>; onRun: () => void }) {
  const running = proof !== null && proof !== 'failed' && proof.status === 'running';
  const result = proof !== null && proof !== 'failed' ? proof.result : null;
  return (
    <article className={styles.wide} data-testid="proof-card">
      <h2 className={styles.cardTitle}>Same performance, proven</h2>
      <p className={styles.muted}>
        The same requests a visitor makes, sent by this container to itself on both stores in paired
        rounds that alternate which store goes first: identical process, identical request, only the
        store differs. Each row is one path; the difference is the median of the paired differences,
        and the last column but one takes one round trip per store operation off each side, so the
        difference the stores make can be told from the difference their distance makes. Two
        throwaway accounts, one per store, and about half a minute.
      </p>
      <p>
        <button
          type="button"
          className={styles.back}
          onClick={onRun}
          disabled={running}
          data-testid="proof-run"
        >
          {running ? 'Running…' : result === null ? 'Run the proof' : 'Run it again'}
        </button>
      </p>
      {proof === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : proof === 'failed' ? (
        <p className={styles.muted} data-testid="card-failed">
          Could not read the proof on the last try; the next try is in 30 seconds.
        </p>
      ) : result === null ? (
        <p className={styles.muted} data-testid="proof-note">
          {running
            ? 'Running. The result lands here within a minute.'
            : 'Not run yet on this container. The button runs it; the result stays until the next deploy.'}
        </p>
      ) : result.status === 'failed' ? (
        <p className={styles.muted} data-testid="proof-note">
          {result.reason}
        </p>
      ) : (
        <>
          <p data-testid="proof-sentence">{result.sentence}</p>
          <p className={styles.muted}>
            {result.rounds} paired rounds, finished{' '}
            {result.finished_at ? new Date(result.finished_at).toLocaleTimeString() : ''}. One round
            trip to the store:{' '}
            {result.stores.map((s) => `${s.hop_ms ?? '?'} ms to ${s.name}`).join(', ')}.
          </p>
          <div className={styles.tableWrap} role="region" aria-label="The proof" tabIndex={0}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">Path</th>
                  {result.stores.map((s) => (
                    <th scope="col" key={s.key}>
                      {s.name}
                    </th>
                  ))}
                  <th scope="col">Difference</th>
                  <th scope="col">Without the round trips</th>
                  <th scope="col">Verdict</th>
                </tr>
              </thead>
              <tbody>
                {result.rows.map((row) => (
                  <tr key={row.path}>
                    <td>{row.label}</td>
                    {row.cells.map((cell) => (
                      <td className={styles.mono} key={cell.store}>
                        {cell.samples === 0
                          ? 'not measured'
                          : `p50 ${cell.p50_ms} ms, p95 ${cell.p95_ms} ms (${cell.samples})` +
                            (cell.request_units_per_request === null
                              ? ''
                              : ` · ${cell.request_units_per_request} RU`) +
                            (cell.operations_per_request > 0
                              ? `, ${cell.operations_per_request} ops`
                              : '')}
                      </td>
                    ))}
                    <td className={styles.mono}>
                      {row.median_difference_ms === null ? '' : signed(row.median_difference_ms)}
                    </td>
                    <td className={styles.mono}>
                      {row.difference_without_hops_ms === null
                        ? ''
                        : signed(row.difference_without_hops_ms)}
                    </td>
                    <td>{row.verdict}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </article>
  );
}

/** A difference with its sign, so a column of them reads as a column. */
function signed(ms: number): string {
  return ms > 0 ? `+${ms} ms` : `${ms} ms`;
}
// #endregion proof-card

// #region comparison
/** One column of the comparison: a store, wherever it runs, on the rows the card draws. */
type Column = {
  title: string;
  store: string;
  startup: Startup;
  requests: { window: number; p50_ms: number; p95_ms: number; by_route: RouteTiming[] };
  charges: RouteCharge[];
  storeOps: string;
};

function storeOpsLine(store: StoreSummary | null, sql: SqlSummary | null): string {
  if (store !== null) {
    return `p50 ${store.p50_ms} ms, p95 ${store.p95_ms} ms, ${store.ru_total} RU over ${store.window}, ${store.cross_partition} cross-partition`;
  }
  if (sql !== null) {
    return `p50 ${sql.p50_ms} ms, p95 ${sql.p95_ms} ms over ${sql.window} statements`;
  }
  return '';
}

/** A whole container's metrics as one column, which is what a peer answers with. */
function columnOf(title: string, m: Metrics): Column {
  const cosmos = m.store.store === 'Azure Cosmos DB';
  return {
    title,
    store: m.store.store,
    startup: m.startup,
    requests: m.requests,
    charges: cosmos ? m.store_by_route : [],
    storeOps: storeOpsLine(cosmos ? m.store : null, cosmos ? null : m.sql),
  };
}

/** One store of a container that runs more than one (ADR: One container, both stores). */
function columnOfBackend(backend: BackendMetrics, current: boolean): Column {
  return {
    title: current ? `${backend.store}, serving this visit` : backend.store,
    store: backend.store,
    startup: backend.startup,
    requests: backend.requests,
    charges: backend.store_by_route,
    storeOps: storeOpsLine(backend.store_metrics, backend.sql),
  };
}

/**
 * The columns the card compares. A container running both stores compares
 * them with each other, in one process, the current one first; a container
 * running one compares itself with its peer, whatever /api/admin/peer relayed,
 * and when it relayed nothing the column says why and the rest of the card
 * stands (ADR: Backends, side by side).
 */
function columns(mine: Metrics, peer: Peer | null): { columns: Column[]; note: string | null } {
  const backends = mine.backends ?? [];
  if (backends.length > 1) {
    const current = backends.find((b) => b.store === mine.store.store) ?? backends[0];
    const ordered = [current, ...backends.filter((b) => b !== current)];
    return {
      columns: ordered.map((b) => columnOfBackend(b, b === current)),
      note: 'Both stores run in this container, so the two columns share a process, a region and a request ring; only the store differs.',
    };
  }
  const theirs = peer !== null && peer.reachable ? peer.metrics : null;
  const peerTitle =
    peer === null
      ? 'The peer could not be read'
      : !peer.configured
        ? 'No peer is configured on this container'
        : peer.reachable
          ? `The other site (${peer.metrics?.store.store ?? 'store unknown'})`
          : `The other site is not answering`;
  const peerNote = peer !== null && peer.configured && !peer.reachable ? peer.reason : null;
  const empty: Column = {
    title: peerTitle,
    store: '',
    startup: {
      ...mine.startup,
      ready_ms: null,
      schema_ms: 0,
      seed_ms: 0,
      seed_ru: null,
      catalogue_ms: null,
      bids_ms: null,
    },
    requests: { window: 0, p50_ms: 0, p95_ms: 0, by_route: [] },
    charges: [],
    storeOps: '',
  };
  return {
    columns: [
      columnOf(`This site (${mine.store.store})`, mine),
      theirs === null ? empty : columnOf(peerTitle, theirs),
    ],
    note: peerNote,
  };
}

function Comparison({ mine, peer }: { mine: Metrics; peer: Peer | null }) {
  const { columns: cols, note } = columns(mine, peer);
  const blank = (column: Column) => column.store === '';

  const routeCell = (column: Column, route: string): string => {
    if (blank(column)) return '';
    const timing = column.requests.by_route.find((r) => r.route === route);
    if (timing === undefined) return 'not seen yet';
    const charge = column.charges.find((r) => r.route === route);
    const time = `p50 ${timing.p50_ms} ms, p95 ${timing.p95_ms} ms (${timing.count})`;
    return charge === undefined ? time : `${time} · ${charge.ru_p50} RU`;
  };

  const rows: { label: string; cells: (column: Column) => string }[] = [
    { label: 'Store', cells: (c) => c.store },
    {
      label: 'Cold start, process start to ready',
      cells: (c) => (blank(c) ? '' : ms(c.startup.ready_ms)),
    },
    {
      label: 'Store check (schema or containers)',
      cells: (c) => (blank(c) ? '' : ms(c.startup.schema_ms)),
    },
    {
      label: 'Seed, first boot only',
      cells: (c) => (blank(c) ? '' : msAndRu(c.startup.seed_ms, c.startup.seed_ru)),
    },
    { label: 'Catalogue load', cells: (c) => (blank(c) ? '' : ms(c.startup.catalogue_ms)) },
    { label: 'Bids load', cells: (c) => (blank(c) ? '' : ms(c.startup.bids_ms)) },
    {
      label: 'Requests, all paths',
      cells: (c) =>
        blank(c)
          ? ''
          : `p50 ${c.requests.p50_ms} ms, p95 ${c.requests.p95_ms} ms (${c.requests.window})`,
    },
    ...COMPARED_ROUTES.map((entry) => ({
      label: entry.label,
      cells: (c: Column) => routeCell(c, entry.route),
    })),
    { label: 'Store operations, this window', cells: (c) => c.storeOps },
  ];

  return (
    <div className={styles.tableWrap} role="region" aria-label="Backends side by side" tabIndex={0}>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">Measured</th>
            {cols.map((column) => (
              <th scope="col" key={column.title}>
                {column.title}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.label}>
              <td>{row.label}</td>
              {cols.map((column) => (
                <td className={styles.mono} key={column.title}>
                  {row.cells(column)}
                </td>
              ))}
            </tr>
          ))}
          {note !== null ? (
            <tr>
              <td className={styles.muted} colSpan={cols.length + 1} data-testid="peer-note">
                {note}
              </td>
            </tr>
          ) : null}
        </tbody>
      </table>
    </div>
  );
}
// #endregion comparison

// #region store-card
/** The document store's counterpart of the SQL card: every operation with its partition and its charge (ADR: What the store is actually doing). */
function StoreCard({ log }: { log: StoreLog }) {
  return (
    <article className={styles.wide} data-testid="store-card">
      <h2 className={styles.cardTitle}>What the document store ran</h2>
      <p className={styles.muted}>
        Every operation this container sent to {log.store}, newest first: the container, whether it
        was a point read, a point write, a query or a batch, whether it was pinned to one partition
        or fanned out across every physical partition, and what it cost in request units beside how
        long it took. Parameters are listed by name, type and size and never by value, and a
        partition is described rather than named, because this page is public and the key of an
        account&rsquo;s partition is the account. Operations caused by this page and by the health
        check are left out. The buffer holds the last 200 in this container&rsquo;s memory and
        empties on every deploy.
      </p>
      {log.operations.length === 0 ? (
        <p className={styles.muted}>Nothing recorded yet.</p>
      ) : (
        <div
          className={styles.tableWrap}
          role="region"
          aria-label="Operations this application sent to the document store"
          tabIndex={0}
        >
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">At</th>
                <th scope="col">Took</th>
                <th scope="col">Charge</th>
                <th scope="col">Caused by</th>
                <th scope="col">Container</th>
                <th scope="col">Kind</th>
                <th scope="col">Partition</th>
                <th scope="col">Operation</th>
                <th scope="col">Parameters</th>
              </tr>
            </thead>
            <tbody>
              {log.operations.slice(0, 60).map((operation, index) => (
                <tr key={index}>
                  <td className={styles.mono}>{new Date(operation.at).toLocaleTimeString()}</td>
                  <td className={styles.mono}>{operation.duration_ms} ms</td>
                  <td className={styles.mono}>{operation.request_charge} RU</td>
                  <td className={styles.mono}>{operation.request ?? 'startup'}</td>
                  <td className={styles.mono}>{operation.container}</td>
                  <td>{operation.kind}</td>
                  <td>
                    {operation.partition}
                    {operation.partition.startsWith('cross')
                      ? ` (${operation.physical_partitions} physical)`
                      : ''}
                  </td>
                  <td>
                    <pre className={styles.sql}>{operation.text}</pre>
                    <span className={styles.muted}>{operation.outcome}</span>
                  </td>
                  <td className={styles.mono}>{describeParameters(operation.parameters)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </article>
  );
}
// #endregion store-card
