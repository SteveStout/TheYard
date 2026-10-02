/**
 * The timing the Admin tab computes on read from its rings, as the Timing,
 * Backends, SQL and Store cards draw it, and the same answer relayed from the
 * other container. Types only. One file because the peer answer wraps the
 * metrics answer, and the per-backend rows reuse its parts.
 */

/**
 * One request path: how many calls, and its median, slow-end and worst times.
 * Sent by GET /api/admin/metrics, in the requests by path.
 */
export type EndpointTiming = {
  path: string;
  count: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
};

/**
 * One response status and how many requests answered with it.
 * Sent by GET /api/admin/metrics, in the counts by status.
 */
export type StatusCount = { status: number; count: number };

/**
 * One route pattern: how many calls, and its median, slow-end and worst times.
 * Sent by GET /api/admin/metrics, in the requests by route and in each backend.
 */
export type RouteTiming = {
  route: string;
  count: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
};

/**
 * The document store's window: its times, what it charged, and how many operations fanned out or hit one item.
 * Sent by GET /api/admin/metrics, as the store block and in each backend.
 */
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

/**
 * One route's store cost: requests, operations per request, its charge and how often it fanned out.
 * Sent by GET /api/admin/metrics, in the store by route and in each backend.
 */
export type RouteCharge = {
  route: string;
  requests: number;
  operations_per_request: number;
  ru_p50: number;
  ru_max: number;
  cross_partition: number;
};

/**
 * How long a store took to come up, step by step, and when it started; a step that did not run is null.
 * Sent by GET /api/admin/metrics, as the startup block and in each backend.
 */
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

/**
 * The SQL ring's window: how many statements, and their median, slow-end and worst times.
 * Sent by GET /api/admin/metrics, as the sql block and in each backend.
 */
export type SqlSummary = { window: number; p50_ms: number; p95_ms: number; max_ms: number };

/**
 * One store this container runs, on the rows the comparison card draws (ADR: One container, both stores).
 * Sent by GET /api/admin/metrics, in the backends.
 */
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

/**
 * The whole timing answer: requests by path and route, counts by status, both stores, startup and each backend.
 * Sent by GET /api/admin/metrics.
 */
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

/**
 * The other container's metrics, read server side: whether it is set up and reachable, and its answer if it was.
 * Sent by GET /api/admin/peer (ADR: Backends, side by side).
 */
export type Peer = {
  configured: boolean;
  reachable: boolean;
  reason: string | null;
  host: string | null;
  fetched_at: string;
  metrics: Metrics | null;
};
