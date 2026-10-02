/**
 * What the three machines under the site are doing, as the Machines and Traffic
 * cards read it: the container from its runtime, the relational store from its
 * resource view, the document store from what its operations charged (ADR: What
 * the machines are doing). Types only. Its own file because it is one endpoint.
 */
import type { KeptBucket, TrafficMinute } from '../../../../lib/machineChart';

/**
 * One reading of the container's memory, processor, threads and garbage collections.
 * Sent by GET /api/admin/machines, in the container's samples.
 */
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

/**
 * One row of the relational store's own resource view, each figure a percent of what the tier allows.
 * Sent by GET /api/admin/machines, in the relational rows.
 */
export type ResourceStatRow = {
  at: string;
  cpu_percent: number;
  data_io_percent: number;
  log_write_percent: number;
  memory_percent: number;
  worker_percent: number;
};

/**
 * One minute of the document store: what its operations charged, how many there were, and the share of the free allowance.
 * Sent by GET /api/admin/machines, in the document minutes.
 */
export type DocumentMinute = {
  at: string;
  request_units: number;
  operations: number;
  share_of_free_percent: number;
};

/**
 * The whole machines answer: the container, both stores, and, where offered, a kept window and the request traffic.
 * Sent by GET /api/admin/machines (with ?window= for a kept window).
 */
export type Machines = {
  /** The windows the endpoint offers, and the kept one it answered with; absent from an older build. */
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
  /** The request ring a minute at a time; absent from an older build. */
  traffic?: { ring: number; minutes: TrafficMinute[] };
  container: {
    memory_limit_mb: number;
    processors: number;
    uptime_seconds: number;
    every_seconds: number;
    samples: MachineSample[];
    /** Which catalogues the process is holding; absent from an older build. */
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
