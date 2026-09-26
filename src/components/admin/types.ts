/**
 * The shapes the Admin tab's endpoints answer with, shared by its cards (ADR:
 * The Admin tab, as a product, the addendum on the workbench). Types only, so a
 * card's chunk that imports them carries nothing for it.
 */
import type { KeptBucket, TrafficMinute } from '../../lib/machineChart';

export type HealthCheck = { name: string; status: string; detail: string; duration_ms: number };

export type Health = {
  status: string;
  uptime_seconds: number;
  version: string;
  commit: string;
  checks: HealthCheck[];
};

export type ErrorEntry = {
  at: string;
  path: string;
  status: number;
  message: string;
  frames: string[];
};

export type PageEntry = {
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

export type PageReport = {
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

export type PageStatus = { status: string; report: PageReport | null };

export type MachineSample = {
  at: string;
  working_set_mb: number;
  managed_mb: number;
  heap_mb: number;
  cpu_percent: number | null;
  threads: number;
  gen0_collections: number;
  gen2_collections: number;
};

export type ResourceStatRow = {
  at: string;
  cpu_percent: number;
  data_io_percent: number;
  log_write_percent: number;
  memory_percent: number;
  worker_percent: number;
};

export type DocumentMinute = {
  at: string;
  request_units: number;
  operations: number;
  share_of_free_percent: number;
};

export type Machines = {
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

export type AzureEvent = { name: string; count: number; last_at: string; message: string };

export type TelemetrySummary = {
  total: number;
  failed: number;
  p50_ms: number | null;
  p95_ms: number | null;
};

export type TelemetryRoute = { name: string; calls: number; avg_ms: number | null };

export type TelemetryException = { type: string; method: string; count: number; last_at: string };

export type TelemetryBrowser = { count: number; last_at: string };

export type Telemetry = {
  configured: boolean;
  available?: boolean;
  note?: string;
  window?: string;
  summary?: TelemetrySummary;
  slowest?: TelemetryRoute[];
  exceptions?: TelemetryException[];
  browser?: TelemetryBrowser;
  /** The newest request of the last day, or null when there is none (1.0.3.31). */
  newest_request_at?: string | null;
};

export type SqlParameterShape = { name: string; type: string; size: number | null };

export type SqlStatement = {
  at: string;
  text: string;
  parameters: SqlParameterShape[];
  duration_ms: number;
  outcome: string;
  request: string | null;
};

export type LogEntry = {
  at: string;
  level: string;
  category: string;
  message: string;
  exception: string | null;
};

export type EndpointTiming = {
  path: string;
  count: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
};

export type StatusCount = { status: number; count: number };

export type RouteTiming = {
  route: string;
  count: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
};

export type StoreSummary = {
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

export type RouteCharge = {
  route: string;
  requests: number;
  operations_per_request: number;
  ru_p50: number;
  ru_max: number;
  cross_partition: number;
};

export type Startup = {
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

export type SqlSummary = { window: number; p50_ms: number; p95_ms: number; max_ms: number };

/** One store this container runs, on the rows the comparison card draws (ADR: One container, both stores). */
export type BackendMetrics = {
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

export type Metrics = {
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

export type Peer = {
  configured: boolean;
  reachable: boolean;
  reason: string | null;
  host: string | null;
  fetched_at: string;
  metrics: Metrics | null;
};

export type StoreOperation = {
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

export type StoreLog = { store: string; operations: StoreOperation[] };

export type ExperimentRow = {
  query: string;
  partitions: string;
  request_charge: number;
  duration_ms: number;
  documents: number;
};

export type Experiment = {
  available: boolean;
  reason: string | null;
  container?: string;
  physical_partitions?: number;
  documents?: number;
  rows: ExperimentRow[];
  ran_at?: string;
};

export type AzureState = {
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

/** A card's data: nothing yet, the value, or the word that the last fetch failed (ADR-017). */
export type Fetched<T> = T | null | 'failed';

export type ProofCell = {
  store: string;
  samples: number;
  p50_ms: number;
  p95_ms: number;
  operations_per_request: number;
  request_units_per_request: number | null;
  failures: number;
};

export type ProofRow = {
  path: string;
  label: string;
  cells: ProofCell[];
  median_difference_ms: number | null;
  difference_without_hops_ms: number | null;
  verdict: string;
};

export type ProofResult = {
  status: 'done' | 'failed';
  reason: string | null;
  started_at: string | null;
  finished_at: string | null;
  rounds: number;
  stores: { key: string; name: string; hop_ms: number | null }[];
  rows: ProofRow[];
  sentence: string;
};

export type Proof = { status: 'idle' | 'running' | 'done' | 'failed'; result: ProofResult | null };
