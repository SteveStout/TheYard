import { type ReactNode, useCallback, useEffect, useState } from 'react';
import {
  ACTIVITY_WINDOWS,
  CHART,
  areaPath,
  ceilingOf,
  dayLines,
  groupByDay,
  labelFor,
  labelledIndexes,
  linePath,
  sortVisitors,
  type ActivityReport,
  type ActivityVisitors,
  type ActivityWindow,
  type VisitorSortKey,
} from '../lib/activity';
import { browserStorage, forgetAdminKey, rememberAdminKey, resolveAdminKey } from '../lib/adminKey';
import { shortenDigests } from '../lib/format';
import {
  LOG_KINDS,
  countsLine,
  describe as describeEvent,
  groupEventsByDay,
  queryFor,
  toneOf,
  type KeptLogs,
  type LogFilter,
  type LogKind,
} from '../lib/logs';
import {
  axisLabel,
  ceilingFor,
  clockLabel,
  type ChartSeries,
  coverage,
  fromFirstReading,
  hourOfTraffic,
  type KeptBucket,
  keptSparks,
  keptTraffic,
  LEAST_SLOTS,
  MACHINE_CHART,
  MACHINE_WINDOWS,
  type MachineWindow,
  pairedBars,
  pathFor,
  requestUnitsAMinute,
  shareOf,
  ticks,
  timeline,
  type TrafficMinute,
  type TrafficSlot,
  trafficTotals,
  windowName,
} from '../lib/machineChart';
import {
  CARD_WINDOWS,
  type CardWindow,
  cardUrl,
  cardWindowName,
  everyCardOn,
  type KeptAnswer,
  type KeptCard,
  keptLine,
  type KeptMeta,
  metaOf,
  stampFor,
} from '../lib/keptCards';
import { documentStore, documentStoreLine, sqlLine, timingWindow } from '../lib/metrics';
import {
  QUESTIONS,
  afterColdStart,
  sparkCaption,
  sparkRuns,
  type StatTile,
  tilesFrom,
  type TileQuestion,
  visitorsOn,
} from '../lib/statTiles';
import styles from './AdminPanel.module.css';

type HealthCheck = { name: string; status: string; detail: string; duration_ms: number };
type Health = {
  status: string;
  uptime_seconds: number;
  version: string;
  commit: string;
  checks: HealthCheck[];
};
type ErrorEntry = {
  at: string;
  path: string;
  status: number;
  message: string;
  frames: string[];
};
type PageEntry = {
  address: string;
  what: string;
  kind: string;
  status: number;
  ms: number;
  bytes: number;
  content_type: string | null;
  reason: string | null;
  ok: boolean;
};
type PageReport = {
  at: string;
  trigger: string;
  version: string;
  commit: string;
  ms: number;
  checked: number;
  up: number;
  failed: string | null;
  entries: PageEntry[];
};
type PageStatus = { status: string; report: PageReport | null };
type MachineSample = {
  at: string;
  working_set_mb: number;
  managed_mb: number;
  heap_mb: number;
  cpu_percent: number | null;
  threads: number;
  gen0_collections: number;
  gen2_collections: number;
};
type ResourceStatRow = {
  at: string;
  cpu_percent: number;
  data_io_percent: number;
  log_write_percent: number;
  memory_percent: number;
  worker_percent: number;
};
type DocumentMinute = {
  at: string;
  request_units: number;
  operations: number;
  share_of_free_percent: number;
};
type Machines = {
  /** The windows the endpoint offers, and the kept one it answered with; absent on a build older than 1.0.0.159. */
  windows?: string[];
  history?: {
    window: string;
    kept: boolean;
    available: boolean;
    note: string | null;
    site: string;
    bucket_minutes: number;
    as_of: string;
    buckets: KeptBucket[];
  };
  /** The request ring a minute at a time; absent on a build older than 1.0.0.160. */
  traffic?: { ring: number; minutes: TrafficMinute[] };
  container: {
    memory_limit_mb: number;
    processors: number;
    uptime_seconds: number;
    every_seconds: number;
    samples: MachineSample[];
    /** Which catalogues the process is holding; absent on a build older than 1.0.0.158. */
    catalogues?: { store: string; serves: boolean; loaded: boolean }[];
  };
  relational: { store: string; available: boolean; note: string | null; rows: ResourceStatRow[] };
  document: {
    store: string;
    available: boolean;
    note: string | null;
    request_units: number;
    operations: number;
    p50_ms: number | null;
    p95_ms: number | null;
    free_request_units_per_second: number;
    minutes: DocumentMinute[];
  };
};
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
  /** Which kind of machine answered: a container group, or a web app on a shared plan (ADR: One plan, two sites). */
  host?: 'container-instances' | 'app-service';
  group_state?: string;
  container_state?: string;
  restart_count?: number;
  image?: string;
  events?: AzureEvent[];
  availability?: string;
  always_on?: boolean;
  health_check_path?: string | null;
  plan_name?: string | null;
  plan_sku?: string | null;
  plan_sites?: number | null;
  region?: string | null;
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
/**
 * The operator's key, read from the address bar once, when the module loads,
 * and otherwise from what this browser remembered (src/lib/adminKey.ts).
 * Once and not per render, because the app mirrors its own view into the
 * address bar and drops anything it did not put there, which takes the key
 * out of the URL on the first render; that is welcome, since a key in an
 * address bar outlives the tab in the history, and it means the key has to be
 * read before that mirror runs (the 1.0.0.114 gate, take one). Remembered,
 * because the file the key lives in is on one machine and the operator
 * reads the site from his phone (the 1.0.0.120 change).
 */
const ADMIN_KEY = resolveAdminKey(window.location.search, browserStorage());

export function AdminPanel({
  onBack,
  signedIn,
  onOpenAccount,
}: {
  onBack: () => void;
  signedIn: boolean;
  onOpenAccount: () => void;
}) {
  // The operator's key: state, so forgetting it takes effect on the cards
  // at once; its first value is the one read when the module loaded.
  const [adminKey, setAdminKey] = useState<string | null>(ADMIN_KEY);
  const forgetKey = () => {
    forgetAdminKey(browserStorage());
    setAdminKey(null);
  };
  const enterKey = (entered: string) => {
    const key = rememberAdminKey(entered, browserStorage());
    if (key !== null) setAdminKey(key);
  };
  // Whether this site serves the per-visitor rows, learned from the activity
  // report; null until it has answered. Off by default (13 September).
  const [rowsServed, setRowsServed] = useState<boolean | null>(null);
  // What the strip of tiles needs from the cards that do their own reading:
  // today's people from the activity report, and the last page sweep.
  const [visitorsToday, setVisitorsToday] = useState<number | null>(null);
  const [pagesSeen, setPagesSeen] = useState<{ checked: number; up: number } | null>(null);
  const onActivity = useCallback((report: ActivityReport) => {
    setRowsServed(report.visitor_rows);
    setVisitorsToday(visitorsOn(report.days, new Date()));
  }, []);
  const {
    machines,
    latest: latestMachines,
    window: machineWindow,
    toolbar: windowToolbar,
  } = useMachines();
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
  // #region kept-cards
  // The four public lists over a window (ADR: Logs that outlive the container,
  // the addendum on the cards). The rings above are always read, because the
  // tiles and the timing are made of them; a card showing a day, a week or a
  // month reads the same entries back from the store beside them.
  const [cardWindows, setCardWindows] = useState<Record<KeptCard, CardWindow>>(
    everyCardOn<CardWindow>('now')
  );
  const [keptMeta, setKeptMeta] = useState<Record<KeptCard, KeptMeta | null>>(
    everyCardOn<KeptMeta | null>(null)
  );
  const [keptErrors, setKeptErrors] = useState<Fetched<ErrorEntry[]>>(null);
  const [keptLogs, setKeptLogs] = useState<Fetched<LogEntry[]>>(null);
  const [keptSql, setKeptSql] = useState<Fetched<SqlStatement[]>>(null);
  const [keptStore, setKeptStore] = useState<Fetched<StoreOperation[]>>(null);
  const chooseWindow = (card: KeptCard, chosen: CardWindow) => {
    if (chosen === cardWindows[card]) return;
    // The change of window is the event, so the card goes back to reading
    // here and not inside the effect, as the machines window does.
    setCardWindows((held) => ({ ...held, [card]: chosen }));
    setKeptMeta((held) => ({ ...held, [card]: null }));
    if (card === 'errors') setKeptErrors(null);
    if (card === 'logs') setKeptLogs(null);
    if (card === 'sql') setKeptSql(null);
    if (card === 'store') setKeptStore(null);
  };
  // #endregion kept-cards

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

  useEffect(() => {
    let live = true;
    const read = <T,>(card: KeptCard, set: (rows: Fetched<T[]>) => void) => {
      const chosen = cardWindows[card];
      if (chosen === 'now') return;
      void fetch(cardUrl(card, chosen, ''))
        .then((r) =>
          r.ok ? (r.json() as Promise<KeptAnswer<T>>) : Promise.reject(new Error(String(r.status)))
        )
        .then((answer) => {
          if (!live) return;
          set(answer.entries);
          setKeptMeta((held) => ({ ...held, [card]: metaOf(answer, chosen) }));
        })
        .catch(() => {
          if (live) set('failed');
        });
    };
    read<ErrorEntry>('errors', setKeptErrors);
    read<LogEntry>('logs', setKeptLogs);
    read<SqlStatement>('sql', setKeptSql);
    read<StoreOperation>('store', setKeptStore);
    return () => {
      live = false;
    };
  }, [tick, cardWindows]);

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

  // #region tiles
  // The strip is made of what the cards below have already read, by the rules
  // in statTiles.ts; it asks the server for nothing of its own, so a tile and
  // the card it points at cannot disagree.
  const seen = latestMachines;
  const hour = seen === null ? null : hourSlots(seen);
  // The lines under the tiles follow the window every chart follows, once the
  // answer for that window has arrived and the store keeps it.
  const keptHistory =
    seen !== null &&
    machineWindow !== '1h' &&
    seen.history !== undefined &&
    seen.history.window === machineWindow &&
    seen.history.available
      ? seen.history
      : null;
  const keptLines =
    keptHistory === null || machineWindow === '1h'
      ? null
      : keptSparks(
          fromFirstReading(
            timeline(
              keptHistory.buckets,
              machineWindow,
              keptHistory.bucket_minutes,
              new Date(keptHistory.as_of)
            )
          )
        );
  const lastSample =
    seen !== null && seen.container.samples.length > 0
      ? seen.container.samples[seen.container.samples.length - 1]
      : null;
  // The minutes of a cold start are not held against the hour (statTiles.ts,
  // the cold-start region). When the process started is its newest sample's
  // time less its uptime, both from the one answer, so nothing here reads a clock.
  const startedAt =
    seen === null || lastSample === null
      ? null
      : new Date(new Date(lastSample.at).getTime() - seen.container.uptime_seconds * 1000);
  const warmed = hour === null ? null : afterColdStart(hour, startedAt);
  // Every request and every error in the hour is still counted; only the
  // slowest ninety-fifth is read from the warm minutes.
  const warmTotals = warmed === null ? null : trafficTotals(warmed.warm, 1);
  const hourTotals =
    hour === null || warmTotals === null
      ? null
      : {
          ...trafficTotals(hour, 1),
          slowest_p95_ms: warmTotals.slowest_p95_ms,
          slowest_at: warmTotals.slowest_at,
        };
  const coldStart =
    warmed !== null && warmed.left_out.some((slot) => (slot.requests ?? 0) > 0)
      ? clockLabel(warmed.left_out[0].at)
      : null;
  const tiles = tilesFrom({
    health: health !== null && health !== 'failed' ? health : null,
    pages: pagesSeen,
    traffic:
      hourTotals === null
        ? null
        : {
            ...hourTotals,
            slowest_label:
              hourTotals.slowest_at === null ? null : clockLabel(hourTotals.slowest_at),
            cold_start_label: coldStart,
          },
    memory:
      seen === null || lastSample === null
        ? null
        : { working_set_mb: lastSample.working_set_mb, limit_mb: seen.container.memory_limit_mb },
    charged:
      seen === null
        ? null
        : seen.document.available
          ? {
              request_units: seen.document.request_units,
              free_per_second: seen.document.free_request_units_per_second,
            }
          : 'none',
    errors: errors !== null && errors !== 'failed' ? errors.length : null,
    visitorsToday,
    sparks:
      seen === null
        ? undefined
        : keptLines !== null
          ? { ...keptLines, charged: seen.document.available ? keptLines.charged : undefined }
          : {
              speed: hour?.map((slot) => slot.p95_ms),
              memory: seen.container.samples.map((sample) => sample.working_set_mb),
              charged: seen.document.available
                ? seen.document.minutes.map((minute) => minute.request_units)
                : undefined,
              errors: hour?.map((slot) => slot.server_errors),
            },
  });
  // #endregion tiles

  const errorRows = cardWindows.errors === 'now' ? errors : keptErrors;
  const logRows = cardWindows.logs === 'now' ? logs : keptLogs;
  const sqlRows = cardWindows.sql === 'now' ? sql : keptSql;
  const picker = (card: KeptCard) => (
    <CardWindowPicker
      card={card}
      value={cardWindows[card]}
      meta={keptMeta[card]}
      onChoose={(chosen) => chooseWindow(card, chosen)}
    />
  );

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
        The running system reporting on itself, in the order somebody asks: is it up, is it fast, is
        it costing anything, what broke.
      </p>
      <About>
        The tiles are made of what the cards below have read, and each one goes to the cards that
        answer its question. Every statistic the page shows for one store it shows for the other,
        and where a number has no meaning on one side the page says so in words. Refreshes every 30
        seconds. Public on purpose; the reasoning is in the Best Practices menu.
      </About>
      <StatStrip
        tiles={tiles}
        toolbar={windowToolbar('over the tiles', 'strip-window')}
        caption={sparkCaption(
          windowName(machineWindow).toLowerCase(),
          machineWindow === '1h'
            ? 'hour'
            : keptLines !== null
              ? 'kept'
              : latestMachines?.history?.window === machineWindow
                ? 'not-kept'
                : 'reading'
        )}
      />

      <section className={styles.question} aria-labelledby="question-up" data-testid="question-up">
        <h2 className={styles.questionTitle} id="question-up">
          {QUESTIONS.up}
        </h2>
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
            ) : azure.available && azure.host === 'app-service' ? (
              // A web app on a shared plan reports a different set of facts from a
              // container group, and the card shows the set it has: the site's
              // state, the image it was told to run, and the plan both sites share
              // (ADR: One plan, two sites).
              <>
                <ul className={styles.checkList}>
                  <li className={styles.checkRow}>
                    <span className={pill(azure.group_state === 'Running')}>
                      {azure.group_state}
                    </span>
                    <span>
                      web app{azure.region ? `, ${azure.region}` : ''}, availability{' '}
                      {azure.availability?.toLowerCase()}
                    </span>
                  </li>
                  <li className={styles.checkRow}>
                    <span className={styles.mono}>{azure.plan_name ?? 'plan unread'}</span>
                    <span className={styles.muted}>
                      {azure.plan_sku ?? 'size unread'}
                      {typeof azure.plan_sites === 'number'
                        ? `, ${azure.plan_sites} site${azure.plan_sites === 1 ? '' : 's'} sharing it`
                        : ''}
                    </span>
                  </li>
                  <li className={styles.checkRow}>
                    <span className={styles.mono}>{azure.image?.split('/').pop()}</span>
                    <span className={styles.muted}>image Azure reports</span>
                  </li>
                  <li className={styles.checkRow}>
                    <span className={pill(azure.always_on === true)}>
                      {azure.always_on ? 'Always On' : 'Always On is off'}
                    </span>
                    <span className={styles.muted}>
                      {azure.health_check_path
                        ? `the platform asks ${azure.health_check_path} and replaces an instance that stops answering`
                        : 'no health check path is set'}
                    </span>
                  </li>
                </ul>
                <p className={styles.muted}>
                  App Service keeps no restart count and no container events where this site's
                  identity can read them, so neither is shown. Uptime on the health card is the
                  restart story here.
                </p>
              </>
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
        </div>
        <PagesCard onReport={setPagesSeen} />
      </section>

      <section
        className={styles.question}
        aria-labelledby="question-fast"
        data-testid="question-fast"
      >
        <h2 className={styles.questionTitle} id="question-fast">
          {QUESTIONS.fast}
        </h2>
        <TrafficSection
          machines={machines}
          window={machineWindow}
          toolbar={windowToolbar('on the traffic card', 'machines-window')}
        />
        <div className={styles.grid}>
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
                        <li
                          key={index}
                          className={styles.errorRow}
                          data-testid="telemetry-exception"
                        >
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

        {/* #region backends-card */}
        <article className={styles.wide} data-testid="backends-card">
          <h2 className={styles.cardTitle}>Backends, side by side</h2>
          <About>
            The two stores on the same rows: how long each took to come up, what the seed cost, and
            how long the things a visitor does take on each, with the request charge beside every
            number the document store can put one on. A container that runs both stores compares
            them with each other, in one process, the one serving this visit first; a container that
            runs one compares itself with its peer, read through its own API with two and a half
            seconds of patience, so a peer that is down is a sentence here and not a hang. Cold
            start and seed are measured on each store at its own start; the rest is the last few
            hundred requests each has served.
          </About>
          {metrics === null || peer === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : metrics === 'failed' ? (
            failed('the comparison')
          ) : (
            <Comparison mine={metrics} peer={peer === 'failed' ? null : peer} />
          )}
        </article>
        {/* #endregion backends-card */}

        <ProofCard
          proof={proof}
          signedIn={signedIn}
          onRun={() => void runProof()}
          onOpenAccount={onOpenAccount}
        />
      </section>

      <section
        className={styles.question}
        aria-labelledby="question-cost"
        data-testid="question-cost"
      >
        <h2 className={styles.questionTitle} id="question-cost">
          {QUESTIONS.cost}
        </h2>
        <MachinesCard
          machines={machines}
          window={machineWindow}
          toolbar={windowToolbar('on the machines card', 'machines-card-window')}
        />
        {/* #region experiment-card */}
        <article className={styles.wide} data-testid="experiment-card">
          <h2 className={styles.cardTitle}>The partition key, live</h2>
          <About>
            Seven queries against a container of 100,000 vehicles partitioned on the make, run by
            this container with its own identity when this page asks, and cached for a minute. A
            query that names the make runs inside one logical partition; one that cannot fans out
            across every physical partition, and the request charge beside each is what that costs.
            The reasoning, the alternatives and the honest caveat about how many physical partitions
            there are at this size are in the partition key record.
          </About>
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
          <StoreCard
            log={store}
            rows={cardWindows.store === 'now' ? store.operations : keptStore}
            window={cardWindows.store}
            picker={picker('store')}
          />
        ) : null}
        {store === null ||
        store === 'failed' ||
        store.store !== 'Azure Cosmos DB' ||
        (metrics !== null &&
          metrics !== 'failed' &&
          (metrics.backends ?? []).some((backend) => backend.sql !== null)) ? (
          <article className={styles.wide} data-testid="sql-card">
            <h2 className={styles.cardTitle}>The SQL this application ran</h2>
            <About>
              Every statement Entity Framework sent, newest first, with the request that caused it
              and how long the database took. Parameters are listed by name, type and size. Their
              values are not here and never were: the type this table is built from has no field to
              put one in, because this page is public and a registration&rsquo;s parameters carry an
              email address. The request is the method and the path, without its query string, for
              the same reason. Statements caused by this page and by the health check are left out,
              or watching would be all there was to see. The buffer holds the last 200 in this
              container&rsquo;s memory and empties on every deploy.
            </About>
            {picker('sql')}
            {sqlRows === null ? (
              <p className={styles.muted}>Loading…</p>
            ) : sqlRows === 'failed' ? (
              failed('the SQL log')
            ) : sqlRows.length === 0 ? (
              cardWindows.sql !== 'now' ? null : (
                <p className={styles.muted}>
                  Nothing recorded yet. The catalogue is read once at startup and cached, so an idle
                  container runs no SQL at all.
                </p>
              )
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
                    {sqlRows
                      .slice(0, cardWindows.sql === 'now' ? 60 : 200)
                      .map((statement, index) => (
                        <tr key={index}>
                          <td className={styles.mono}>{stampFor(cardWindows.sql, statement.at)}</td>
                          <td className={styles.mono}>{statement.duration_ms} ms</td>
                          <td className={styles.mono}>{statement.request ?? 'startup'}</td>
                          <td>
                            <pre className={styles.sql}>{statement.text}</pre>
                            <span className={styles.muted}>{statement.outcome}</span>
                          </td>
                          <td className={styles.mono}>
                            {describeParameters(statement.parameters)}
                          </td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            )}
          </article>
        ) : null}
        {/* #endregion sql-section */}
      </section>

      <section
        className={styles.question}
        aria-labelledby="question-broke"
        data-testid="question-broke"
      >
        <h2 className={styles.questionTitle} id="question-broke">
          {QUESTIONS.broke}
        </h2>
        <div className={styles.grid}>
          <article className={`${styles.card} ${styles.wideCard}`} data-testid="errors-card">
            <h2 className={styles.cardTitle}>Recent errors</h2>
            {picker('errors')}
            {errorRows === null ? (
              <p className={styles.muted}>Loading…</p>
            ) : errorRows === 'failed' ? (
              failed('the error list')
            ) : errorRows.length === 0 ? (
              cardWindows.errors !== 'now' ? null : (
                <p className={styles.muted}>
                  None recorded since the container started, from the server or the browser. The
                  buffer holds the last 50 and resets on every deploy; Application Insights keeps
                  the durable copy (ADR: Telemetry). A server error carries its stack, file and line
                  beside it; the exception&rsquo;s message is deliberately not here, because a
                  message is where a framework writes a connection detail and this page is public.
                </p>
              )
            ) : (
              <div
                className={styles.tableWrap}
                role="region"
                aria-label="Recent errors"
                tabIndex={0}
              >
                <table className={styles.table} data-testid="errors-table">
                  <thead>
                    <tr>
                      <th scope="col">At</th>
                      <th scope="col">Status</th>
                      <th scope="col">Where</th>
                      <th scope="col">What</th>
                      <th scope="col">Stack</th>
                    </tr>
                  </thead>
                  <tbody>
                    {errorRows.map((entry, index) => (
                      <tr key={index}>
                        <td className={styles.mono}>{stampFor(cardWindows.errors, entry.at)}</td>
                        <td className={styles.mono}>
                          {entry.status === 0 ? 'browser' : entry.status}
                        </td>
                        <td className={styles.mono}>{entry.path}</td>
                        <td>{entry.message}</td>
                        <td>
                          {entry.frames.length === 0 ? (
                            <span className={styles.muted}>no stack</span>
                          ) : (
                            <details data-testid={`error-frames-${index}`}>
                              <summary>{entry.frames.length} frames</summary>
                              <pre className={styles.sql}>{entry.frames.join('\n')}</pre>
                            </details>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </article>
        </div>
        {/* #region log-section */}
        <article className={styles.wide} data-testid="log-card">
          <h2 className={styles.cardTitle}>The log, as the console got it</h2>
          <About>
            This application&rsquo;s own log lines at Information and above, newest first, holding
            the last 300 in memory. Its own, which since 1.0.0.103 includes one line per document
            store operation with its charge and its time, and the one framework category that gives
            every SQL statement a line of its own: the rest of the framework is left out because a
            healthy container announces its content root and its key directory, and those are server
            paths on a public page. An exception shows its type. Its message stays server-side,
            because a database driver writes the server name, the login name and the caller&rsquo;s
            address into one.
          </About>
          {picker('logs')}
          {logRows === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : logRows === 'failed' ? (
            failed('the log')
          ) : logRows.length === 0 ? (
            cardWindows.logs !== 'now' ? null : (
              <p className={styles.muted}>Nothing recorded since the container started.</p>
            )
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
                  {logRows.slice(0, cardWindows.logs === 'now' ? 80 : 200).map((entry, index) => (
                    <tr key={index}>
                      <td className={styles.mono}>{stampFor(cardWindows.logs, entry.at)}</td>
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
        {rowsServed === true && <KeptLogsCard adminKey={adminKey} />}
      </section>

      <section
        className={styles.question}
        aria-labelledby="question-desk"
        data-testid="question-desk"
      >
        <h2 className={styles.questionTitle} id="question-desk">
          Who came, and the operator&rsquo;s desk
        </h2>
        <ActivityCard adminKey={adminKey} rowsServed={rowsServed} onReport={onActivity} />
        <OperatorCard adminKey={adminKey} onEnterKey={enterKey} onForget={forgetKey} />
        <ResetLinkCard adminKey={adminKey} />
      </section>
    </section>
  );
}

// #region stat-strip
/**
 * The strip across the top (ADR: The Admin tab, as a product): eight tiles
 * under four questions, each a button that goes to the cards that answer it.
 * What a tile says and what colour it is are decided in statTiles.ts, which
 * has no React in it; this only draws. The tone is a word as well as a
 * colour, because a colour alone says nothing to somebody who cannot see it.
 */
const QUESTION_ORDER: TileQuestion[] = ['up', 'fast', 'cost', 'broke'];
const TONE_WORD: Record<StatTile['tone'], string | null> = {
  good: 'fine',
  warn: 'worth a look',
  bad: 'needs attention',
  plain: null,
  waiting: 'waiting',
};

function StatStrip({
  tiles,
  toolbar,
  caption,
}: {
  tiles: StatTile[];
  toolbar: ReactNode;
  caption: string;
}) {
  const toneClass: Record<StatTile['tone'], string> = {
    good: styles.tileGood,
    warn: styles.tileWarn,
    bad: styles.tileBad,
    plain: styles.tilePlain,
    waiting: styles.tileWaiting,
  };
  const ordered = QUESTION_ORDER.flatMap((question) =>
    tiles.filter((tile) => tile.question === question)
  );
  return (
    <>
      <div className={styles.stripHead}>
        {toolbar}
        <p className={styles.muted} data-testid="strip-caption">
          {caption}
        </p>
      </div>
      <ul className={styles.strip} aria-label="The site at a glance" data-testid="stat-strip">
        {ordered.map((tile) => {
          const runs = tile.spark === undefined ? [] : sparkRuns(tile.spark, 100, 24);
          const word = TONE_WORD[tile.tone];
          return (
            <li key={tile.key} className={styles.stripItem}>
              <button
                type="button"
                className={`${styles.tile} ${toneClass[tile.tone]}`}
                data-testid={`tile-${tile.key}`}
                data-tone={tile.tone}
                onClick={() =>
                  document.getElementById(`question-${tile.question}`)?.scrollIntoView()
                }
              >
                <span className={styles.tileQuestion}>{QUESTIONS[tile.question]}</span>
                <span className={styles.tileLabel}>{tile.label}</span>
                <span className={styles.tileValue}>{tile.value}</span>
                <span className={styles.tileDetail}>{tile.detail}</span>
                {word !== null && <span className={styles.tileTone}>{word}</span>}
                {runs.length > 0 && (
                  <svg
                    className={styles.spark}
                    viewBox="0 0 100 24"
                    preserveAspectRatio="none"
                    aria-hidden="true"
                    focusable="false"
                  >
                    {runs.map((points) => (
                      <polyline key={points} points={points} />
                    ))}
                  </svg>
                )}
              </button>
            </li>
          );
        })}
      </ul>
    </>
  );
}
// #endregion stat-strip

// #region card-window-picker
/**
 * Now, a day, a week or a month, on one of the four public lists (ADR: Logs
 * that outlive the container, the addendum on the cards), and the sentence
 * that says what the table under it is showing.
 */
function CardWindowPicker({
  card,
  value,
  meta,
  onChoose,
}: {
  card: KeptCard;
  value: CardWindow;
  meta: KeptMeta | null;
  onChoose: (chosen: CardWindow) => void;
}) {
  return (
    <>
      <p className={styles.statusRow} role="group" aria-label={`Window for the ${card} card`}>
        {CARD_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === value}
            onClick={() => onChoose(option)}
            data-testid={`kept-window-${card}-${option}`}
          >
            {cardWindowName(option)}
          </button>
        ))}
      </p>
      <p className={styles.muted} data-testid={`kept-line-${card}`}>
        {keptLine(value, meta)}
      </p>
    </>
  );
}
// #endregion card-window-picker

/** What a card is and how to read it, folded away: the numbers come first, and the paragraph is one tap off. */
function About({ children }: { children: ReactNode }) {
  return (
    <details className={styles.about}>
      <summary className={styles.aboutSummary}>What this shows</summary>
      <p className={styles.muted}>{children}</p>
    </details>
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
// #region activity-card
/**
 * Site activity (ADR: Site activity, and the line an address does not cross).
 * The graph at the top of the tab: requests over time, the two stores as two
 * lines on one axis, drawn as an inline SVG in the palette the drawings under
 * docs/images use, with the totals, the split and the top paths beside it.
 * It names nobody, so it is as public as the rest of the tab.
 *
 * The table under it is not public. It fetches only when the address bar
 * carries a key, sends the key with the request, and the endpoint answers
 * 404 to everybody else, so without the key the table does not exist here
 * any more than it exists on the wire.
 */
function ActivityCard({
  adminKey,
  rowsServed,
  onReport,
}: {
  adminKey: string | null;
  rowsServed: boolean | null;
  onReport: (report: ActivityReport) => void;
}) {
  const [window_, setWindow] = useState<ActivityWindow>('7d');
  const [report, setReport] = useState<Fetched<ActivityReport>>(null);
  const [visitors, setVisitors] = useState<Fetched<ActivityVisitors>>(null);
  const [sortKey, setSortKey] = useState<VisitorSortKey>('last_seen');
  const [descending, setDescending] = useState(true);
  const key = adminKey;

  useEffect(() => {
    let live = true;
    void fetch(`/api/admin/activity?window=${window_}`)
      .then((r) =>
        r.ok ? (r.json() as Promise<ActivityReport>) : Promise.reject(new Error(String(r.status)))
      )
      .then((v) => {
        if (live) {
          setReport(v);
          onReport(v);
        }
      })
      .catch(() => {
        if (live) setReport('failed');
      });
    if (key !== null && rowsServed === true) {
      void fetch(`/api/admin/activity/visitors?window=${window_}`, {
        headers: { 'X-Admin-Key': key },
      })
        .then((r) =>
          r.ok
            ? (r.json() as Promise<ActivityVisitors>)
            : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) setVisitors(v);
        })
        .catch(() => {
          if (live) setVisitors('failed');
        });
    }
    return () => {
      live = false;
    };
  }, [window_, key, rowsServed, onReport]);

  const sortBy = (next: VisitorSortKey) => {
    if (next === sortKey) {
      setDescending((d) => !d);
    } else {
      setSortKey(next);
      setDescending(next !== 'network' && next !== 'store');
    }
  };

  return (
    <article className={styles.wide} data-testid="activity-card">
      <h2 className={styles.cardTitle}>Site activity</h2>
      <About>
        Unique visitors per day, everybody and each store, from rows kept in Azure Cosmos DB by both
        sites, each row naming the store that served it, so the two stores show against each other
        and a paused relational database cannot take this card down with it. Under it, each
        visitor's day: when they came, how many requests, which store, and what they asked for.
        Written off the request path in batches; the page's own files, the photos and this tab's
        reads are not counted. A visitor is a keyed hash of the address that changes daily, so the
        counts group and nothing joins across days or back to a person; a full address is never
        stored and no account is ever named. The table of visitors is behind a key only the operator
        holds.
      </About>
      <p className={styles.statusRow} role="group" aria-label="Window">
        {ACTIVITY_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === window_}
            onClick={() => {
              // The change of window is the event; the cards go back to
              // loading here rather than inside the effect that fetches. The
              // window already showing is not a change: the effect would not
              // run again and the card would stay on "Loading" for good
              // (the 1.0.0.116 gate, take one).
              if (option === window_) return;
              setWindow(option);
              setReport(null);
              setVisitors(null);
            }}
            data-testid={`activity-window-${option}`}
          >
            {option === '24h' ? 'Last 24 hours' : option === '7d' ? 'Last 7 days' : 'Last 30 days'}
          </button>
        ))}
      </p>
      {report === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : report === 'failed' ? (
        <p className={styles.muted} data-testid="card-failed">
          Could not read the activity on the last try; the next try is on the next window change.
        </p>
      ) : (
        <ActivityGraph report={report} />
      )}
      {key !== null && rowsServed === true && (
        <>
          <h3 className={styles.cardTitle}>Visitors</h3>
          {visitors === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : visitors === 'failed' ? (
            <p className={styles.muted} data-testid="visitors-refused">
              The visitor rows did not answer to this key.
            </p>
          ) : (
            <div
              className={styles.tableWrap}
              role="region"
              aria-label="Visitors in the window"
              tabIndex={0}
            >
              <table className={styles.table} data-testid="activity-visitors">
                <thead>
                  <tr>
                    <th scope="col">Visitor</th>
                    <SortHeader
                      label="Network"
                      column="network"
                      current={sortKey}
                      onSort={sortBy}
                    />
                    <SortHeader label="Store" column="store" current={sortKey} onSort={sortBy} />
                    <SortHeader
                      label="First seen"
                      column="first_seen"
                      current={sortKey}
                      onSort={sortBy}
                    />
                    <SortHeader
                      label="Last seen"
                      column="last_seen"
                      current={sortKey}
                      onSort={sortBy}
                    />
                    <SortHeader
                      label="Requests"
                      column="requests"
                      current={sortKey}
                      onSort={sortBy}
                    />
                    <th scope="col">Top paths</th>
                  </tr>
                </thead>
                <tbody>
                  {groupByDay(visitors.visitors).flatMap((group) => [
                    <tr
                      key={`day:${group.day}`}
                      className={styles.dayRow}
                      data-testid="activity-day"
                    >
                      <th scope="rowgroup" colSpan={7}>
                        {labelFor(group.day, '30d')} ({group.day}): {group.visitors} visitor
                        {group.visitors === 1 ? '' : 's'}, {group.requests} request
                        {group.requests === 1 ? '' : 's'}
                      </th>
                    </tr>,
                    ...sortVisitors(group.rows, sortKey, descending).map((row) => (
                      <tr key={`${row.store}:${row.day}:${row.visitor}`}>
                        <td className={styles.mono}>{row.visitor.slice(0, 12)}</td>
                        <td className={styles.mono}>{row.network}</td>
                        <td>{row.store}</td>
                        <td className={styles.mono}>{new Date(row.first_seen).toLocaleString()}</td>
                        <td className={styles.mono}>{new Date(row.last_seen).toLocaleString()}</td>
                        <td className={styles.mono}>
                          {row.requests}
                          {row.bots > 0 ? ` (${row.bots} bot)` : ''}
                        </td>
                        <td className={styles.mono}>
                          {row.top_paths
                            .map((entry) => `${entry.path} (${entry.requests})`)
                            .join(', ')}
                        </td>
                      </tr>
                    )),
                  ])}
                  {visitors.visitors.length === 0 && (
                    <tr>
                      <td colSpan={7} className={styles.muted}>
                        Nobody in this window.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </article>
  );
}

/**
 * The kept log (ADR: Logs that outlive the container): every request, error
 * and warning, written to the document store off the request path and kept
 * for three years, read back here behind the operator's key. Without the key the
 * card says what it is and shows nothing, which is the same line the visitor
 * table draws and for the same reason.
 */
function KeptLogsCard({ adminKey }: { adminKey: string | null }) {
  const [window_, setWindow] = useState<ActivityWindow>('24h');
  const [filter, setFilter] = useState<LogFilter>({ kind: '', status: '', path: '' });
  const [applied, setApplied] = useState<LogFilter>(filter);
  const [logs, setLogs] = useState<Fetched<KeptLogs>>(null);
  const key = adminKey;

  useEffect(() => {
    if (key === null) return;
    let live = true;
    void fetch(`/api/admin/logs/kept?${queryFor(window_, applied)}`, {
      headers: { 'X-Admin-Key': key },
    })
      .then((r) =>
        r.ok ? (r.json() as Promise<KeptLogs>) : Promise.reject(new Error(String(r.status)))
      )
      .then((v) => {
        if (live) setLogs(v);
      })
      .catch(() => {
        if (live) setLogs('failed');
      });
    return () => {
      live = false;
    };
  }, [window_, applied, key]);

  return (
    <article className={styles.wide} data-testid="kept-logs-card">
      <h2 className={styles.cardTitle}>Kept log</h2>
      <About>
        Every request, every error and every warning, written to Azure Cosmos DB off the request
        path in batches and kept for three years, so the log outlives the container and the thirty
        days Application Insights keeps. The same rule as the visitor table: a request is a token
        that changes daily and a network to three octets, an error is its type, its message and a
        bounded stack, and no field can carry an at sign. Behind a key only the operator holds.
      </About>
      {key === null ? (
        <p className={styles.muted} data-testid="kept-logs-keyless">
          The kept log answers only to the operator's key.
        </p>
      ) : (
        <>
          <p className={styles.statusRow} role="group" aria-label="Window">
            {ACTIVITY_WINDOWS.map((option) => (
              <button
                key={option}
                type="button"
                className={styles.back}
                aria-pressed={option === window_}
                onClick={() => {
                  if (option === window_) return;
                  setWindow(option);
                  setLogs(null);
                }}
                data-testid={`kept-logs-window-${option}`}
              >
                {option === '24h'
                  ? 'Last 24 hours'
                  : option === '7d'
                    ? 'Last 7 days'
                    : 'Last 30 days'}
              </button>
            ))}
          </p>
          <form
            className={styles.filterRow}
            aria-label="Narrow the kept log"
            onSubmit={(event) => {
              event.preventDefault();
              setApplied(filter);
              setLogs(null);
            }}
          >
            <label>
              Kind{' '}
              <select
                value={filter.kind}
                onChange={(event) =>
                  setFilter({ ...filter, kind: event.target.value as LogKind | '' })
                }
                data-testid="kept-logs-kind"
              >
                <option value="">any</option>
                {LOG_KINDS.map((kind) => (
                  <option key={kind} value={kind}>
                    {kind}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Status{' '}
              <input
                inputMode="numeric"
                placeholder="any"
                value={filter.status}
                onChange={(event) => setFilter({ ...filter, status: event.target.value })}
                data-testid="kept-logs-status"
              />
            </label>
            <label>
              Path contains{' '}
              <input
                placeholder="any"
                value={filter.path}
                onChange={(event) => setFilter({ ...filter, path: event.target.value })}
                data-testid="kept-logs-path"
              />
            </label>
            <button type="submit" className={styles.back} data-testid="kept-logs-apply">
              Apply
            </button>
          </form>
          {logs === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : logs === 'failed' ? (
            <p className={styles.muted} data-testid="kept-logs-refused">
              The kept log did not answer to this key.
            </p>
          ) : (
            <>
              <p className={styles.muted} data-testid="kept-logs-summary">
                {logs.kept.available
                  ? `${logs.kept.reason}: ${countsLine(logs.counts)} in the window, ${logs.count} shown`
                  : `Nothing is kept on this container: ${logs.kept.reason}.`}{' '}
                Collector: {logs.collector.written} written, {logs.collector.failed_batches} failed
                batches, every {logs.collector.interval_seconds} seconds.
              </p>
              <div
                className={styles.tableWrap}
                role="region"
                aria-label="Kept log in the window"
                tabIndex={0}
              >
                <table className={styles.table} data-testid="kept-logs">
                  <thead>
                    <tr>
                      <th scope="col">When</th>
                      <th scope="col">Kind</th>
                      <th scope="col">Store</th>
                      <th scope="col">What</th>
                      <th scope="col">Network</th>
                      <th scope="col">Detail</th>
                    </tr>
                  </thead>
                  <tbody>
                    {groupEventsByDay(logs.events).flatMap((group) => [
                      <tr
                        key={`day:${group.day}`}
                        className={styles.dayRow}
                        data-testid="kept-logs-day"
                      >
                        <th scope="rowgroup" colSpan={6}>
                          {labelFor(group.day, '30d')} ({group.day}): {group.events.length} line
                          {group.events.length === 1 ? '' : 's'}
                        </th>
                      </tr>,
                      ...group.events.map((e, index) => {
                        const tone = toneOf(e);
                        return (
                          <tr
                            key={`${e.at}:${e.trace_id}:${index}`}
                            className={
                              tone === 'error'
                                ? styles.errorLine
                                : tone === 'warn'
                                  ? styles.warnLine
                                  : undefined
                            }
                          >
                            <td className={styles.mono}>{new Date(e.at).toLocaleTimeString()}</td>
                            <td>{e.kind}</td>
                            <td>{e.store || '(none)'}</td>
                            <td className={styles.mono}>{describeEvent(e)}</td>
                            <td className={styles.mono}>{e.network}</td>
                            <td className={styles.detail}>
                              {e.kind === 'request' ? e.trace_id : e.detail || e.category}
                            </td>
                          </tr>
                        );
                      }),
                    ])}
                    {logs.events.length === 0 && (
                      <tr>
                        <td colSpan={6} className={styles.muted}>
                          Nothing in this window.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </>
      )}
    </article>
  );
}

/**
 * The operator's key on this browser (ADR: Site activity, and the line an
 * address does not cross, fifth addendum): typed into the page, because a
 * link that loses its query string on the way to a phone leaves this box as
 * the way in; remembered on this browser; forgotten on request for a shared
 * device. The cards behind the key (the visitor rows when this site serves
 * them, the reset links) show once it is known.
 */
function OperatorCard({
  adminKey,
  onEnterKey,
  onForget,
}: {
  adminKey: string | null;
  onEnterKey: (entered: string) => void;
  onForget: () => void;
}) {
  const [entered, setEntered] = useState('');
  return (
    <article className={styles.wide} data-testid="operator-card">
      <h2 className={styles.cardTitle}>Operator</h2>
      {adminKey === null ? (
        <>
          <p className={styles.muted}>
            The operator's cards answer only to the operator's key. Type it here once and this
            browser remembers it.
          </p>
          <form
            className={styles.filterRow}
            aria-label="Enter the operator's key"
            onSubmit={(event) => {
              event.preventDefault();
              onEnterKey(entered);
              setEntered('');
            }}
          >
            <label>
              Operator's key{' '}
              <input
                type="password"
                autoComplete="off"
                value={entered}
                onChange={(event) => setEntered(event.target.value)}
                data-testid="admin-key-entry"
              />
            </label>
            <button type="submit" className={styles.back} data-testid="admin-key-submit">
              Remember it on this browser
            </button>
          </form>
        </>
      ) : (
        <p className={styles.muted}>
          This browser remembers the key, so the operator's cards show without it in the address
          bar.{' '}
          <button
            type="button"
            className={styles.back}
            onClick={onForget}
            data-testid="admin-forget-key"
          >
            Forget the key on this browser
          </button>
        </p>
      )}
    </article>
  );
}

/**
 * A password reset link, minted by the operator for one account on this
 * site's store (ADR: Accounts and per-user bids, addendum). Behind the key,
 * because minting a link for somebody else's account is exactly what a
 * stranger must not be able to do. The operator hands the link to the person
 * by whatever means they have; the link works once, for an hour.
 */
function ResetLinkCard({ adminKey }: { adminKey: string | null }) {
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [made, setMade] = useState<{ email: string; url: string; expires_at: string } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  if (adminKey === null) return null;

  return (
    <article className={styles.wide} data-testid="reset-link-card">
      <h2 className={styles.cardTitle}>Reset a password</h2>
      <About>
        Mint a reset link for an account on this site's store and hand it to the person. The link
        works once, for an hour, and dies the moment the password changes. An emailed link is the
        same mechanism with a sender in front of it.
      </About>
      <form
        className={styles.filterRow}
        aria-label="Mint a reset link"
        onSubmit={(event) => {
          event.preventDefault();
          setBusy(true);
          setMessage(null);
          setMade(null);
          void fetch('/api/admin/reset-links', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Admin-Key': adminKey },
            body: JSON.stringify({ email }),
          })
            .then(async (r) => {
              if (r.ok) {
                setMade((await r.json()) as { email: string; url: string; expires_at: string });
                return;
              }
              const body = (await r.json().catch(() => ({}))) as { detail?: string };
              setMessage(body.detail ?? `The link was not made (${r.status}).`);
            })
            .catch(() => setMessage('The server could not be reached.'))
            .finally(() => setBusy(false));
        }}
      >
        <label>
          Email{' '}
          <input
            type="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            data-testid="reset-link-email"
          />
        </label>
        <button
          type="submit"
          className={styles.back}
          disabled={busy}
          data-testid="reset-link-submit"
        >
          Mint a link
        </button>
      </form>
      {message && (
        <p className={styles.muted} role="alert" data-testid="reset-link-refused">
          {message}
        </p>
      )}
      {made && (
        <p className={styles.muted} data-testid="reset-link-made">
          For {made.email}, until {new Date(made.expires_at).toLocaleTimeString()}:{' '}
          <code className={styles.mono} data-testid="reset-link-url">
            {made.url}
          </code>
        </p>
      )}
    </article>
  );
}

function SortHeader({
  label,
  column,
  current,
  onSort,
}: {
  label: string;
  column: VisitorSortKey;
  current: VisitorSortKey;
  onSort: (column: VisitorSortKey) => void;
}) {
  return (
    <th scope="col" aria-sort={current === column ? 'other' : 'none'}>
      <button type="button" className={styles.sortButton} onClick={() => onSort(column)}>
        {label}
      </button>
    </th>
  );
}

/** The two lines, the axis, the totals and the top paths; the arithmetic is in src/lib/activity.ts. */
function ActivityGraph({ report }: { report: ActivityReport }) {
  // Unique visitors per UTC day: everybody as one line, and one line per
  // store underneath it, so the split shows against the whole.
  const lines = dayLines(
    report.days,
    report.series.map((line) => line.store)
  );
  const ceiling = ceilingOf(lines);
  const points = lines[0]?.points ?? [];
  const labels = labelledIndexes(points.length);
  const innerWidth = CHART.width - CHART.left - CHART.right;
  const step = points.length <= 1 ? 0 : innerWidth / (points.length - 1);
  const colour = (store: string) =>
    store === 'cosmos' ? styles.cosmosLine : store === 'sql' ? styles.sqlLine : styles.allLine;
  const totalVisitors = report.days.reduce((sum, day) => sum + day.visitors, 0);
  const humanVisitors = report.days.reduce((sum, day) => sum + day.humans, 0);
  return (
    <>
      <svg
        className={styles.chart}
        viewBox={`0 0 ${CHART.width} ${CHART.height}`}
        role="img"
        aria-label={`Unique visitors per day over the ${report.window} window, everybody as one line and one line per store`}
        data-testid="activity-graph"
      >
        <line
          className={styles.axis}
          x1={CHART.left}
          y1={CHART.height - CHART.bottom}
          x2={CHART.width - CHART.right}
          y2={CHART.height - CHART.bottom}
        />
        <line
          className={styles.axis}
          x1={CHART.left}
          y1={CHART.top}
          x2={CHART.left}
          y2={CHART.height - CHART.bottom}
        />
        <text className={styles.axisLabel} x={CHART.left - 4} y={CHART.top + 4} textAnchor="end">
          {ceiling}
        </text>
        <text
          className={styles.axisLabel}
          x={CHART.left - 4}
          y={CHART.height - CHART.bottom}
          textAnchor="end"
        >
          0
        </text>
        {labels.map((index) => (
          <text
            key={index}
            className={styles.axisLabel}
            x={CHART.left + index * step}
            y={CHART.height - 8}
            textAnchor={index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle'}
          >
            {points[index] ? labelFor(points[index].at, report.window) : ''}
          </text>
        ))}
        {lines.map((line) => (
          <g
            key={line.store}
            className={colour(line.store)}
            data-testid={`activity-line-${line.store}`}
          >
            <path className={styles.area} d={areaPath(line.points, ceiling)} />
            <path className={styles.line} d={linePath(line.points, ceiling)} />
          </g>
        ))}
      </svg>
      <ul className={styles.summaryList} data-testid="activity-totals">
        <li>
          <span className={`${styles.swatch} ${styles.allLine}`} aria-hidden="true" />
          {totalVisitors.toLocaleString()} unique visitors across the days in the window,{' '}
          {humanVisitors} of them looking like people; {report.totals.requests.toLocaleString()}{' '}
          requests in the window, {report.totals.bots} of them from what looked like scanners and
          crawlers.
        </li>
        {report.by_store.map((store) => (
          <li key={store.store}>
            <span className={`${styles.swatch} ${colour(store.store)}`} aria-hidden="true" />
            {report.series.find((line) => line.store === store.store)?.name ?? store.store}:{' '}
            {report.days
              .reduce(
                (sum, day) =>
                  sum + (day.by_store.find((entry) => entry.store === store.store)?.visitors ?? 0),
                0
              )
              .toLocaleString()}{' '}
            visitor-days, {store.requests.toLocaleString()} requests, {store.bots} of them bots.
          </li>
        ))}
        {report.stores
          .filter((store) => !store.available)
          .map((store) => (
            <li key={store.store} data-testid="activity-unavailable">
              {store.name} keeps no activity here: {store.reason}.
            </li>
          ))}
        <li>
          Top paths:{' '}
          {report.top_paths.length === 0
            ? 'none yet'
            : report.top_paths.map((entry) => `${entry.path} (${entry.requests})`).join(', ')}
          .
        </li>
        <li className={styles.muted}>
          Collector: {report.collector.offered.toLocaleString()} hits offered since the process
          started, {report.collector.written.toLocaleString()} written,{' '}
          {report.collector.failed_batches} batches failed, one batch per store every{' '}
          {report.collector.interval_seconds} seconds.
        </li>
      </ul>
    </>
  );
}
// #endregion activity-card

function ProofCard({
  proof,
  signedIn,
  onRun,
  onOpenAccount,
}: {
  proof: Fetched<Proof>;
  signedIn: boolean;
  onRun: () => void;
  onOpenAccount: () => void;
}) {
  const running = proof !== null && proof !== 'failed' && proof.status === 'running';
  const result = proof !== null && proof !== 'failed' ? proof.result : null;
  return (
    <article className={styles.wide} data-testid="proof-card">
      <h2 className={styles.cardTitle}>Same performance, proven</h2>
      <About>
        The same requests a visitor makes, sent by this container to itself on both stores in paired
        rounds that alternate which store goes first: identical process, identical request, only the
        store differs. Each row is one path; the difference is the median of the paired differences,
        and the last column but one takes one round trip per store operation off each side, so the
        difference the stores make can be told from the difference their distance makes. The proof
        bids with two accounts of its own, one per store, made once and kept, and a run takes about
        half a minute. Reading the result is open to anyone; starting a run is a write, so it takes
        a signed-in visitor, like every other write here.
      </About>
      <p>
        {/* Starting a run writes sixteen bids, so the button follows the one
            rule every write on this site follows (ADR: The one write a
            stranger can make, addendum): signed out, it says what it needs
            and takes the visitor there. It sat disabled at first, a button
            that read like a call to action and did nothing, which on a phone
            reads as broken (Steve, 13 September; ADR: Same performance,
            proven, addendum). */}
        <button
          type="button"
          className={styles.back}
          onClick={signedIn ? onRun : onOpenAccount}
          disabled={running}
          data-testid="proof-run"
        >
          {!signedIn
            ? 'Sign in to run the proof'
            : running
              ? 'Running…'
              : result === null
                ? 'Run the proof'
                : 'Run it again'}
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
          {/* The paired medians as bars, so the eye reads what the table says:
              most pairs are the same length, and the ones that are not differ
              by a round trip (ADR: The Admin tab, as a product). */}
          <ul
            className={styles.pairList}
            aria-label="The paired medians, each bar a share of the longest on the card"
            data-testid="proof-bars"
          >
            {pairedBars(result.rows).map((pair) => (
              <li key={pair.label} className={styles.pairRow}>
                <span className={styles.pairLabel}>{pair.label}</span>
                <span className={styles.pairBars}>
                  {pair.bars.map((bar, index) => (
                    <span key={bar.store} className={styles.pairBarRow}>
                      <svg
                        className={`${styles.pairTrack} ${index === 0 ? styles.sqlLine : styles.cosmosLine}`}
                        viewBox="0 0 100 8"
                        preserveAspectRatio="none"
                        aria-hidden="true"
                      >
                        <rect className={styles.pairBar} width={bar.share} height="8" rx="2" />
                      </svg>
                      <span className={styles.mono}>
                        {bar.ms} ms, {bar.store}
                      </span>
                    </span>
                  ))}
                </span>
              </li>
            ))}
          </ul>
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

// #region machine-chart
/**
 * One resource, one chart (ADR: What the machines are doing, the addendum on
 * drawing them). Up to two lines on one axis, drawn the way the activity graph
 * is drawn and with the same arithmetic split out into src/lib/machineChart.ts.
 * A percentage chart keeps a full axis whatever the hour held, so a quiet hour
 * looks quiet; anything else takes its ceiling from the readings.
 */
function MachineChart({
  testId,
  label,
  series,
  percentage,
  unit,
  window: drawnWindow = '1h',
  tones,
}: {
  testId: string;
  label: string;
  series: ChartSeries[];
  percentage?: boolean;
  unit?: string;
  window?: MachineWindow;
  /** A colour per line by meaning, for the charts where a line is good news or bad; position decides otherwise. */
  tones?: ('plain' | 'good' | 'warn' | 'bad')[];
}) {
  const ceiling = ceilingFor(series, percentage === true ? 100 : 1);
  const points = series[0]?.points ?? [];
  const drawn = series.some((line) => line.points.some((point) => point.value !== null));
  const innerWidth = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  const step = points.length <= 1 ? 0 : innerWidth / (points.length - 1);
  const colour = (index: number) => {
    const tone = tones?.[index];
    if (tone === 'good') return styles.goodLine;
    if (tone === 'warn') return styles.warnLine;
    if (tone === 'bad') return styles.badLine;
    return index === 0 ? styles.allLine : index === 1 ? styles.sqlLine : styles.cosmosLine;
  };

  if (!drawn) {
    return (
      <p className={styles.muted} data-testid={`${testId}-empty`}>
        Nothing to draw yet.
      </p>
    );
  }

  return (
    <>
      <svg
        className={styles.chart}
        viewBox={`0 0 ${MACHINE_CHART.width} ${MACHINE_CHART.height}`}
        role="img"
        aria-label={label}
        data-testid={testId}
      >
        <line
          className={styles.axis}
          x1={MACHINE_CHART.left}
          y1={MACHINE_CHART.height - MACHINE_CHART.bottom}
          x2={MACHINE_CHART.width - MACHINE_CHART.right}
          y2={MACHINE_CHART.height - MACHINE_CHART.bottom}
        />
        <line
          className={styles.axis}
          x1={MACHINE_CHART.left}
          y1={MACHINE_CHART.top}
          x2={MACHINE_CHART.left}
          y2={MACHINE_CHART.height - MACHINE_CHART.bottom}
        />
        <text
          className={styles.axisLabel}
          x={MACHINE_CHART.left - 4}
          y={MACHINE_CHART.top + 4}
          textAnchor="end"
        >
          {ceiling}
          {percentage === true ? '%' : ''}
        </text>
        <text
          className={styles.axisLabel}
          x={MACHINE_CHART.left - 4}
          y={MACHINE_CHART.height - MACHINE_CHART.bottom}
          textAnchor="end"
        >
          0
        </text>
        {ticks(points.length).map((index) => (
          <text
            key={index}
            className={styles.axisLabel}
            x={MACHINE_CHART.left + index * step}
            y={MACHINE_CHART.height - 8}
            textAnchor={index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle'}
          >
            {points[index] ? axisLabel(points[index].at, drawnWindow) : ''}
          </text>
        ))}
        {series.map((line, index) => (
          <g key={line.key} className={colour(index)} data-testid={`${testId}-${line.key}`}>
            <path className={styles.line} d={pathFor(line.points, ceiling)} />
          </g>
        ))}
      </svg>
      <ul className={styles.summaryList}>
        {series.map((line, index) => (
          <li key={line.key}>
            <span className={`${styles.swatch} ${colour(index)}`} aria-hidden="true" />
            {line.name}
            {unit === undefined ? '' : ` (${unit})`}
          </li>
        ))}
      </ul>
    </>
  );
}
// #endregion machine-chart

// #region traffic-card
/**
 * Traffic, drawn (ADR: The Admin tab, as a product). Four questions a person
 * opening this tab is asking, one chart each, in the same frame the machine
 * charts use: how busy is it, how fast is it answering, is anything failing,
 * and over what stretch. The hour is the request ring a minute at a time; a
 * wider window is the minutes each site keeps. Both are turned into the same
 * slots by src/lib/machineChart.ts, so a gap is a gap and a zero is a zero in
 * every window. A build with no traffic block, or a window nothing is kept
 * for, keeps its sentence and draws nothing, rather than pretending to a line.
 */
/** The day and the time a kept window's drawing starts at, for the sentence that says so. */
function startLabel(at: string): string {
  return new Date(at).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * Where a trimmed window's drawing starts, in words. The drawing starts at the
 * first reading unless that would leave fewer than a dozen slots, and then it
 * starts a dozen back; 1.0.0.166 called both "its first reading" and on its
 * first day was wrong by two days, on the live page, in its own sentence.
 */
function youngRecord(drawn: { at: string; bucket: KeptBucket | null }[]): string {
  const first = drawn.find((slot) => slot.bucket !== null);
  return first === undefined || first.at === drawn[0].at
    ? `The record is younger than the window, so the charts start at its first reading, ${startLabel(drawn[0].at)}`
    : `The record is younger than the window: its first reading is ${startLabel(first.at)}, and the charts start ${LEAST_SLOTS} buckets back from now, at ${startLabel(drawn[0].at)}`;
}

/** The hour the request ring holds, as slots; the traffic card and the tiles over the page both read this one. */
function hourSlots(machines: Machines): TrafficSlot[] | null {
  if (machines.traffic === undefined) return null;
  const minutes = machines.traffic.minutes;
  return hourOfTraffic(
    minutes,
    new Date(machines.history?.as_of ?? minutes[minutes.length - 1]?.at ?? 0)
  );
}

function TrafficCard({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Machines;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const history = machines.history;
  const wholeSlots =
    window_ !== '1h' && history !== undefined && history.window === window_ && history.available
      ? timeline(history.buckets, window_, history.bucket_minutes, new Date(history.as_of))
      : null;
  const keptSlots = wholeSlots === null ? null : fromFirstReading(wholeSlots);
  const slots: TrafficSlot[] | null =
    window_ === '1h' ? hourSlots(machines) : keptSlots === null ? null : keptTraffic(keptSlots);
  const minutesPerSlot = window_ === '1h' ? 1 : (history?.bucket_minutes ?? 1);
  const totals = slots === null ? null : trafficTotals(slots, minutesPerSlot);
  const series = (key: string, name: string, pick: (slot: TrafficSlot) => number | null) => ({
    key,
    name,
    points: (slots ?? []).map((slot) => ({ at: slot.at, value: pick(slot) })),
  });

  return (
    <article className={styles.wide} data-testid="traffic-card">
      <h2 className={styles.cardTitle}>Traffic</h2>
      <About>
        How busy the site is, how fast it is answering and whether anything is failing, over the
        window chosen here, which the machines card below follows. The last hour is the request ring
        this process keeps, {machines.traffic?.ring ?? 500} requests deep, a minute at a time; the
        wider windows are the minutes each site keeps in Azure Cosmos DB. This tab&rsquo;s own reads
        and the page sweep are not counted in either.
      </About>
      {toolbar}
      {slots === null || totals === null ? (
        <p className={styles.muted} data-testid="traffic-note">
          {window_ === '1h'
            ? 'This build does not report its traffic a minute at a time.'
            : `${windowName(window_)} is not kept here: ${history?.note ?? 'the store did not answer'}`}
        </p>
      ) : (
        <>
          <p data-testid="traffic-line">
            <strong>{totals.requests} requests</strong> in the {windowName(window_).toLowerCase()},{' '}
            {totals.server_errors} answered 5xx and {totals.client_errors} answered 4xx
            {totals.slowest_p95_ms === null
              ? '.'
              : `, and the slowest stretch answered its ninety-fifth in ${totals.slowest_p95_ms} ms.`}
            {keptSlots !== null &&
              wholeSlots !== null &&
              keptSlots.length < wholeSlots.length &&
              ` ${youngRecord(keptSlots)}.`}
          </p>
          <MachineChart
            testId="traffic-chart-requests"
            label={`Requests a minute over the ${windowName(window_).toLowerCase()}`}
            unit="a minute"
            window={window_}
            series={[series('requests', 'Requests', (slot) => slot.requests)]}
          />
          <MachineChart
            testId="traffic-chart-timing"
            label={`How fast requests were answered over the ${windowName(window_).toLowerCase()}: the median and the ninety-fifth, in milliseconds`}
            unit="ms"
            window={window_}
            tones={['good', 'warn']}
            series={[
              series('p50', 'Median', (slot) => slot.p50_ms),
              series('p95', 'Ninety-fifth', (slot) => slot.p95_ms),
            ]}
          />
          <MachineChart
            testId="traffic-chart-errors"
            label={`Requests answered with an error over the ${windowName(window_).toLowerCase()}, a minute: 5xx is the site failing, 4xx is a request it refused`}
            unit="a minute"
            window={window_}
            tones={['bad', 'warn']}
            series={[
              series('5xx', 'Answered 5xx', (slot) => slot.server_errors),
              series('4xx', 'Answered 4xx', (slot) => slot.client_errors),
            ]}
          />
        </>
      )}
    </article>
  );
}
// #endregion traffic-card

// #region kept-window
/**
 * A day, a week or a month of the machines, from the minutes each site keeps in
 * the document store (ADR: What the machines are doing, the addendum on the
 * windows). Three charts in the units the hour uses, drawn over the whole
 * window slot by slot, so a stretch the store holds nothing for is a gap in
 * the line and not a line drawn across it. A window the store cannot answer
 * says why instead of drawing an empty chart.
 */
function KeptWindow({ machines, window: kept }: { machines: Machines; window: MachineWindow }) {
  const history = machines.history;
  if (kept === '1h' || history === undefined || history.window !== kept) {
    return null;
  }
  if (!history.available) {
    return (
      <p className={styles.muted} data-testid="machines-history-note">
        {windowName(kept)} is not kept here: {history.note}
      </p>
    );
  }

  const whole = timeline(history.buckets, kept, history.bucket_minutes, new Date(history.as_of));
  const held = coverage(whole);
  // Counted against the whole window, drawn from the first reading.
  const slots = fromFirstReading(whole);
  const grain =
    history.bucket_minutes >= 60
      ? `${history.bucket_minutes / 60}-hour`
      : `${history.bucket_minutes}-minute`;
  const peak = history.buckets.reduce(
    (most, bucket) => Math.max(most, bucket.working_set_max_mb),
    0
  );
  const charged = history.buckets.reduce((sum, bucket) => sum + bucket.request_units, 0);

  return (
    <div data-testid="machines-history">
      <p data-testid="machines-history-line">
        <strong>{windowName(kept)}</strong>, in {grain} buckets, from the minutes the{' '}
        {history.site === 'cosmos' ? 'Cosmos DB' : 'SQL'} site has kept: {held.held} of {held.of}{' '}
        buckets hold a reading.{' '}
        {slots.length < whole.length
          ? `${youngRecord(slots)}; a gap after the first reading is drawn as the gap it is.`
          : 'A stretch with no reading is drawn as the gap it is.'}
        {held.held > 0 &&
          ` Peak working set in the window: ${peak} MB. The document store charged ${Math.round(charged * 100) / 100} request units in it.`}
        {history.note !== null && held.held === 0 && ` ${history.note}.`}
      </p>
      <MachineChart
        testId="machine-history-container"
        label={`The container over the ${windowName(kept).toLowerCase()}: memory as a share of its limit, and processor share`}
        percentage
        window={kept}
        series={[
          {
            key: 'memory',
            name: 'Memory, share of the limit',
            points: slots.map((slot) => ({
              at: slot.at,
              value:
                slot.bucket === null
                  ? null
                  : shareOf(slot.bucket.working_set_mb, slot.bucket.memory_limit_mb),
            })),
          },
          {
            key: 'cpu',
            name: 'Processor share',
            points: slots.map((slot) => ({ at: slot.at, value: slot.bucket?.cpu_percent ?? null })),
          },
        ]}
      />
      <MachineChart
        testId="machine-history-relational"
        label={`The relational store over the ${windowName(kept).toLowerCase()}, as it reported itself each minute: processor and memory as shares of what the tier allows`}
        percentage
        window={kept}
        series={[
          {
            key: 'memory',
            name: 'Relational store: memory, share of the tier',
            points: slots.map((slot) => ({
              at: slot.at,
              value: slot.bucket?.sql_memory_percent ?? null,
            })),
          },
          {
            key: 'cpu',
            name: 'Relational store: processor, share of the tier',
            points: slots.map((slot) => ({
              at: slot.at,
              value: slot.bucket?.sql_cpu_percent ?? null,
            })),
          },
        ]}
      />
      <MachineChart
        testId="machine-history-document"
        label={`What the document store charged over the ${windowName(kept).toLowerCase()}, request units a minute`}
        unit="request units a minute"
        window={kept}
        series={[
          {
            key: 'ru',
            name: 'Document store',
            points: slots.map((slot) => ({ at: slot.at, value: requestUnitsAMinute(slot.bucket) })),
          },
        ]}
      />
    </div>
  );
}
// #endregion kept-window

// #region machines-card
/**
 * What the three machines are doing (ADR: What the machines are doing). Three
 * readings with three different honesties, which is why this is one card with
 * three blocks rather than one table pretending they are the same: the
 * container knows its own memory exactly, Azure SQL Database keeps its own
 * reading of itself, and the document store has no memory reading to give and
 * says so.
 */
// #region machines-read
/**
 * One read for the traffic card, the machines card and the tiles over the
 * page, and one window for all three: they are only worth looking at side by
 * side over the same stretch. A hook and not a component since 1.0.0.161,
 * because the two cards now sit under different questions.
 */
function useMachines(): {
  machines: Fetched<Machines>;
  latest: Machines | null;
  window: MachineWindow;
  toolbar: (where: string, testPrefix: string) => ReactNode;
} {
  const [machines, setMachines] = useState<Fetched<Machines>>(null);
  // The last answer that arrived, which a change of window does not take
  // away: the tiles are made of it, and eight tiles going back to "waiting"
  // because somebody asked a chart for a week would be the page flinching.
  const [latest, setLatest] = useState<Machines | null>(null);
  const [window_, setWindow] = useState<MachineWindow>('1h');

  useEffect(() => {
    let live = true;
    const read = () =>
      fetch(window_ === '1h' ? '/api/admin/machines' : `/api/admin/machines?window=${window_}`)
        .then((r) =>
          r.ok ? (r.json() as Promise<Machines>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (!live) return;
          setMachines(v);
          setLatest(v);
        })
        .catch(() => {
          if (live) setMachines('failed');
        });
    void read();
    // The sampler takes a reading every fifteen seconds; the card follows at
    // half a minute, which is the rate the rest of this tab refreshes at.
    const timer = window.setInterval(() => void read(), 30_000);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [window_]);

  // One window, and a row of buttons wherever a chart is: over the tiles, on
  // the traffic card and on the machines card, which sit under different
  // questions and a long scroll apart. Every row is the same state, so
  // pressing one presses all three (ADR: The Admin tab, as a product, the
  // addendum on one window for every chart).
  const toolbar = (where: string, testPrefix: string) => (
    <p className={styles.statusRow} role="group" aria-label={`Window for every chart, ${where}`}>
      {MACHINE_WINDOWS.map((option) => (
        <button
          key={option}
          type="button"
          className={styles.back}
          aria-pressed={option === window_}
          onClick={() => {
            // The change of window is the event, and the cards go back to
            // loading here rather than inside the effect, as the activity
            // card's do and for the reason it gives.
            if (option === window_) return;
            setWindow(option);
            setMachines(null);
          }}
          data-testid={`${testPrefix}-${option}`}
        >
          {windowName(option)}
        </button>
      ))}
    </p>
  );

  return { machines, latest, window: window_, toolbar };
}
// #endregion machines-read

function TrafficSection({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Fetched<Machines>;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  if (machines === null || machines === 'failed') {
    return (
      <article className={styles.wide} data-testid="traffic-card">
        <h2 className={styles.cardTitle}>Traffic</h2>
        {toolbar}
        <p className={styles.muted}>
          {machines === null ? 'Loading…' : 'Could not read the traffic on the last try.'}
        </p>
      </article>
    );
  }
  return <TrafficCard machines={machines} window={window_} toolbar={toolbar} />;
}

function MachinesCard({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Fetched<Machines>;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  if (machines === null || machines === 'failed') {
    return (
      <article className={styles.wide} data-testid="machines-card">
        <h2 className={styles.cardTitle}>What the machines are doing</h2>
        {toolbar}
        {machines === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : (
          <p className={styles.muted} data-testid="machines-failed">
            Could not read the machines on the last try.
          </p>
        )}
      </article>
    );
  }
  return <MachinesBody machines={machines} window={window_} toolbar={toolbar} />;
}

function MachinesBody({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Machines;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const samples = machines.container.samples;
  const latest = samples.length > 0 ? samples[samples.length - 1] : null;
  const peak = samples.reduce((most, sample) => Math.max(most, sample.working_set_mb), 0);
  const newest = machines.relational.rows.length > 0 ? machines.relational.rows[0] : null;
  const worst = machines.relational.rows.reduce(
    (most, row) => Math.max(most, row.cpu_percent, row.data_io_percent, row.log_write_percent),
    0
  );

  return (
    <article className={styles.wide} data-testid="machines-card">
      <h2 className={styles.cardTitle}>What the machines are doing</h2>
      <About>
        The three machines under this site, each reporting the way it actually reports. The
        container knows its own memory and its own processor time, and the limit its share is read
        against is the runtime&rsquo;s own, which is lower than the memory the machine has, and a
        process that passes its own limit is the one that gets collected; Azure SQL Database keeps a
        reading of itself for the last hour, fifteen seconds at a time, free on every tier; Azure
        Cosmos DB has no memory or processor reading to give, because it is sold by request unit, so
        what it shows is what the operations cost against the free allowance. The last hour is this
        container&rsquo;s own, kept in its memory, and empties on every roll. The wider windows are
        a minute at a time, kept in Azure Cosmos DB by each site for thirty-one days, so they
        survive a roll and show one.
      </About>
      {toolbar}
      <p className={styles.muted} data-testid="machines-window-line">
        Showing {windowName(window_).toLowerCase()}. One window for every chart on this tab: the
        buttons here, on the traffic card and over the tiles are the same buttons.
      </p>
      {window_ !== '1h' && <KeptWindow machines={machines} window={window_} />}

      <h3 className={styles.cardTitle}>
        The container{window_ === '1h' ? '' : ', as this process remembers the last hour'}
      </h3>
      {latest === null ? (
        <p className={styles.muted} data-testid="machines-no-samples">
          No sample yet. One is taken every {machines.container.every_seconds} seconds.
        </p>
      ) : (
        <>
          <p data-testid="machines-container-line">
            <strong>
              {latest.working_set_mb} MB of {machines.container.memory_limit_mb} MB
            </strong>{' '}
            in use, {latest.managed_mb} MB of it managed objects,{' '}
            {latest.cpu_percent === null
              ? 'processor share not read yet'
              : `${latest.cpu_percent}% of ${machines.container.processors} processor${machines.container.processors === 1 ? '' : 's'}`}
            , {latest.threads} threads, {latest.gen0_collections} quick collections and{' '}
            {latest.gen2_collections} full ones since this container started. Peak working set in
            the window: {peak} MB.
          </p>
          {machines.container.catalogues && machines.container.catalogues.length > 0 && (
            // Most of that memory is catalogues, a hundred thousand vehicles a
            // store, so the card says which ones the process is holding. The
            // store this site does not serve is loaded on demand and let go
            // when nobody has asked for it in a while (ADR: One plan, two sites).
            <p className={styles.muted} data-testid="machines-catalogues">
              Catalogues in memory:{' '}
              {machines.container.catalogues
                .map(
                  (catalogue) =>
                    `${catalogue.store}, ${catalogue.serves ? 'which this site serves' : 'loaded on demand'}, ${catalogue.loaded ? 'held' : 'not held'}`
                )
                .join('; ')}
              .
            </p>
          )}
          <MachineChart
            testId="machine-chart-container"
            label="The container over the sampled window: memory as a share of its limit, and processor share"
            percentage
            series={[
              {
                key: 'memory',
                name: `Memory, share of ${machines.container.memory_limit_mb} MB`,
                points: samples.map((sample) => ({
                  at: sample.at,
                  value:
                    machines.container.memory_limit_mb > 0
                      ? Math.round(
                          (sample.working_set_mb / machines.container.memory_limit_mb) * 1000
                        ) / 10
                      : null,
                })),
              },
              {
                key: 'cpu',
                name: `Processor, share of ${machines.container.processors}`,
                points: samples.map((sample) => ({ at: sample.at, value: sample.cpu_percent })),
              },
            ]}
          />
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="The container, sampled"
            tabIndex={0}
          >
            <table className={styles.table} data-testid="machines-container-table">
              <thead>
                <tr>
                  <th scope="col">At</th>
                  <th scope="col">Working set</th>
                  <th scope="col">Managed</th>
                  <th scope="col">Heap</th>
                  <th scope="col">Processors</th>
                  <th scope="col">Threads</th>
                </tr>
              </thead>
              <tbody>
                {[...samples]
                  .reverse()
                  .slice(0, 20)
                  .map((sample) => (
                    <tr key={sample.at}>
                      <td className={styles.mono}>{new Date(sample.at).toLocaleTimeString()}</td>
                      <td className={styles.mono}>{sample.working_set_mb} MB</td>
                      <td className={styles.mono}>{sample.managed_mb} MB</td>
                      <td className={styles.mono}>{sample.heap_mb} MB</td>
                      <td className={styles.mono}>
                        {sample.cpu_percent === null ? 'first' : `${sample.cpu_percent}%`}
                      </td>
                      <td className={styles.mono}>{sample.threads}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <h3 className={styles.cardTitle}>{machines.relational.store}</h3>
      {!machines.relational.available ? (
        <p className={styles.muted} data-testid="machines-relational-note">
          {machines.relational.note}
        </p>
      ) : (
        <>
          <p data-testid="machines-relational-line">
            {newest === null ? (
              'The view answered with no rows yet.'
            ) : (
              <>
                <strong>
                  {newest.cpu_percent}% processor, {newest.memory_percent}% memory
                </strong>{' '}
                in the last fifteen seconds, {newest.data_io_percent}% data and{' '}
                {newest.log_write_percent}% log, {newest.worker_percent}% of the workers the tier
                allows. Busiest reading in the window: {worst}%. Every figure is a share of what
                this tier allows, which on Basic is five DTUs.
              </>
            )}
          </p>
          <MachineChart
            testId="machine-chart-relational"
            label="The relational store over the last hour, as it reports itself: processor and memory as shares of what the tier allows"
            percentage
            series={[
              {
                key: 'memory',
                name: 'Memory, share of the tier',
                points: [...machines.relational.rows]
                  .reverse()
                  .map((row) => ({ at: row.at, value: row.memory_percent })),
              },
              {
                key: 'cpu',
                name: 'Processor, share of the tier',
                points: [...machines.relational.rows]
                  .reverse()
                  .map((row) => ({ at: row.at, value: row.cpu_percent })),
              },
            ]}
          />
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="The relational store's own reading"
            tabIndex={0}
          >
            <table className={styles.table} data-testid="machines-relational-table">
              <thead>
                <tr>
                  <th scope="col">At</th>
                  <th scope="col">Processor</th>
                  <th scope="col">Memory</th>
                  <th scope="col">Data</th>
                  <th scope="col">Log</th>
                  <th scope="col">Workers</th>
                </tr>
              </thead>
              <tbody>
                {machines.relational.rows.slice(0, 20).map((row) => (
                  <tr key={row.at}>
                    <td className={styles.mono}>{new Date(row.at).toLocaleTimeString()}</td>
                    <td className={styles.mono}>{row.cpu_percent}%</td>
                    <td className={styles.mono}>{row.memory_percent}%</td>
                    <td className={styles.mono}>{row.data_io_percent}%</td>
                    <td className={styles.mono}>{row.log_write_percent}%</td>
                    <td className={styles.mono}>{row.worker_percent}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <h3 className={styles.cardTitle}>{machines.document.store}</h3>
      {!machines.document.available ? (
        <p className={styles.muted} data-testid="machines-document-note">
          {machines.document.note}
        </p>
      ) : (
        <>
          <p data-testid="machines-document-line">
            <strong>{machines.document.request_units} request units</strong> across{' '}
            {machines.document.operations} operations in the ring, {machines.document.p50_ms} ms at
            the median and {machines.document.p95_ms} ms at the ninety-fifth. The free tier allows{' '}
            {machines.document.free_request_units_per_second} request units a second, and the share
            below is what each minute would be of one second of that. There is no memory or
            processor reading here: the store is sold by request unit and reports neither.
          </p>
          <MachineChart
            testId="machine-chart-document"
            label="What the document store charged, request units a minute"
            unit="request units a minute"
            series={[
              {
                key: 'ru',
                name: 'Request units a minute',
                points: machines.document.minutes.map((minute) => ({
                  at: minute.at,
                  value: minute.request_units,
                })),
              },
            ]}
          />
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="What the document store charged"
            tabIndex={0}
          >
            <table className={styles.table} data-testid="machines-document-table">
              <thead>
                <tr>
                  <th scope="col">Minute</th>
                  <th scope="col">Request units</th>
                  <th scope="col">Operations</th>
                  <th scope="col">Share of a free second</th>
                </tr>
              </thead>
              <tbody>
                {[...machines.document.minutes]
                  .reverse()
                  .slice(0, 20)
                  .map((minute) => (
                    <tr key={minute.at}>
                      <td className={styles.mono}>{new Date(minute.at).toLocaleTimeString()}</td>
                      <td className={styles.mono}>{minute.request_units} RU</td>
                      <td className={styles.mono}>{minute.operations}</td>
                      <td className={styles.mono}>{minute.share_of_free_percent}%</td>
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
// #endregion machines-card

// #region pages-card
/**
 * Every address this site serves, as the container found them (ADR: Every
 * page, checked at every roll). The sweep runs at every roll, so the reading
 * on this card belongs to the build the footer names, and the button runs one
 * now. Failures sort to the top because they are the only rows anybody needs
 * to read; the rest is there so the count can be checked rather than believed.
 */
function PagesCard({ onReport }: { onReport: (seen: { checked: number; up: number }) => void }) {
  const [pages, setPages] = useState<Fetched<PageStatus>>(null);
  const [asked, setAsked] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [showAll, setShowAll] = useState(false);

  useEffect(() => {
    let live = true;
    const read = () =>
      fetch('/api/admin/pages')
        .then((r) =>
          r.ok ? (r.json() as Promise<PageStatus>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) {
            setPages(v);
            if (v.report !== null) onReport({ checked: v.report.checked, up: v.report.up });
          }
          return v;
        })
        .catch(() => {
          if (live) setPages('failed');
          return null;
        });
    void read();
    // While a sweep is running the card follows it; ninety addresses take a
    // second or two, and a card that showed "running" until the next reload
    // would be the wrong answer for longer than the sweep takes.
    const timer = window.setInterval(() => {
      void read().then((v) => {
        if (v !== null && v.status !== 'running') {
          window.clearInterval(timer);
          setAsked(false);
        }
      });
    }, 3000);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [asked, onReport]);

  const report = pages !== null && pages !== 'failed' ? pages.report : null;
  const down = report ? report.entries.filter((entry) => !entry.ok) : [];
  const shown = report
    ? showAll
      ? [...down, ...report.entries.filter((entry) => entry.ok)]
      : down
    : [];

  return (
    <article className={styles.wide} data-testid="pages-card">
      <h2 className={styles.cardTitle}>Every page, checked</h2>
      <About>
        This container asks itself for every address it serves and records what came back: the app,
        the API&rsquo;s own front pages, the three files at the root of the domain, every document
        in the catalogue and every drawing. The list is built from the catalogue rather than kept
        beside it, so a document added tomorrow is checked tomorrow. It runs at every roll, which is
        what makes the reading below belong to the build in the footer, and the button runs one now.
        An address that answers with nothing counts as down, and a document served as anything but
        markdown is a defect this card would have caught in the README weeks ago. This is the
        container&rsquo;s own view, dialled on its loopback; what the edge is serving the public is
        read from outside after every ship.
      </About>
      {pages === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : pages === 'failed' ? (
        <p className={styles.muted} data-testid="pages-failed">
          Could not read the page check on the last try.
        </p>
      ) : (
        <>
          <p className={styles.statusRow}>
            <button
              type="button"
              className={styles.back}
              onClick={() => {
                setNote(null);
                void fetch('/api/admin/pages', { method: 'POST' })
                  .then((r) => {
                    if (r.status === 409) {
                      return r
                        .json()
                        .then((body: { status?: string }) => setNote(body.status ?? 'not now'));
                    }
                    setAsked((a) => !a);
                    return undefined;
                  })
                  .catch(() => setNote('the check could not be started'));
              }}
              data-testid="pages-run"
            >
              Check every page now
            </button>
            {report !== null && (
              <button
                type="button"
                className={styles.back}
                aria-pressed={showAll}
                onClick={() => setShowAll((v) => !v)}
                data-testid="pages-show-all"
              >
                {showAll ? 'Show only what is down' : 'Show every address'}
              </button>
            )}
          </p>
          {note !== null && <p className={styles.muted}>{note}</p>}
          {report === null ? (
            <p className={styles.muted} data-testid="pages-not-run">
              {pages.status === 'running'
                ? 'Checking every address now…'
                : 'No check has run in this container yet. It runs at every roll, and the button above runs one now.'}
            </p>
          ) : report.failed !== null ? (
            <p className={styles.muted}>The last check stopped on {report.failed}.</p>
          ) : (
            <>
              <p data-testid="pages-summary">
                <strong>
                  {report.up} of {report.checked} addresses answered
                </strong>{' '}
                in {report.ms} ms, checked for {report.version} at {report.commit},{' '}
                {report.trigger === 'roll' ? 'on the roll' : 'when asked'}, at{' '}
                {new Date(report.at).toLocaleString()}.
              </p>
              {down.length === 0 && !showAll && (
                <p className={styles.muted} data-testid="pages-all-up">
                  Nothing is down.
                </p>
              )}
              {shown.length > 0 && (
                <div
                  className={styles.tableWrap}
                  role="region"
                  aria-label="Every address this container serves"
                  tabIndex={0}
                >
                  <table className={styles.table} data-testid="pages-table">
                    <thead>
                      <tr>
                        <th scope="col">Address</th>
                        <th scope="col">What</th>
                        <th scope="col">Kind</th>
                        <th scope="col">Answered</th>
                        <th scope="col">Type</th>
                        <th scope="col">Bytes</th>
                        <th scope="col">Took</th>
                      </tr>
                    </thead>
                    <tbody>
                      {shown.map((entry) => (
                        <tr key={entry.address}>
                          <td className={styles.mono}>{entry.address}</td>
                          <td>{entry.what}</td>
                          <td>{entry.kind}</td>
                          <td className={styles.mono}>
                            {entry.status === 0 ? (entry.reason ?? 'no answer') : entry.status}
                          </td>
                          <td className={styles.mono}>{entry.content_type ?? 'none'}</td>
                          <td className={styles.mono}>{entry.bytes.toLocaleString()}</td>
                          <td className={styles.mono}>{entry.ms} ms</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </>
      )}
    </article>
  );
}
// #endregion pages-card

// #region store-card
/** The document store's counterpart of the SQL card: every operation with its partition and its charge (ADR: What the store is actually doing). */
function StoreCard({
  log,
  rows,
  window: window_,
  picker,
}: {
  log: StoreLog;
  rows: Fetched<StoreOperation[]>;
  window: CardWindow;
  picker: ReactNode;
}) {
  return (
    <article className={styles.wide} data-testid="store-card">
      <h2 className={styles.cardTitle}>What the document store ran</h2>
      <About>
        Every operation this container sent to {log.store}, newest first: the container, whether it
        was a point read, a point write, a query or a batch, whether it was pinned to one partition
        or fanned out across every physical partition, and what it cost in request units beside how
        long it took. Parameters are listed by name, type and size and never by value, and a
        partition is described rather than named, because this page is public and the key of an
        account&rsquo;s partition is the account. Operations caused by this page and by the health
        check are left out. The buffer holds the last 200 in this container&rsquo;s memory and
        empties on every deploy.
      </About>
      {picker}
      {rows === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : rows === 'failed' ? (
        <p className={styles.muted} data-testid="card-failed">
          Could not read the store log on the last try; the next try is in 30 seconds.
        </p>
      ) : rows.length === 0 ? (
        window_ !== 'now' ? null : (
          <p className={styles.muted}>Nothing recorded yet.</p>
        )
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
              {rows.slice(0, window_ === 'now' ? 60 : 200).map((operation, index) => (
                <tr key={index}>
                  <td className={styles.mono}>{stampFor(window_, operation.at)}</td>
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
