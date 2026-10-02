/**
 * The performance proof the Proof card draws (ADR: Same performance, proven):
 * the same requests timed against both stores, a row per path and a verdict.
 * Types only. Its own file because it is one endpoint with nested shapes.
 */

/**
 * One store's figures for one path: samples, median and slow-end time, operations and charge per request, failures.
 * Sent by GET /api/admin/proof, in each row's cells.
 */
export type ProofCell = {
  store: string;
  samples: number;
  p50_ms: number;
  p95_ms: number;
  operations_per_request: number;
  request_units_per_request: number | null;
  failures: number;
};

/**
 * One path compared across the stores: each store's cell, the median difference with and without the network hops, and the verdict.
 * Sent by GET /api/admin/proof, in the result's rows.
 */
export type ProofRow = {
  path: string;
  label: string;
  cells: ProofCell[];
  median_difference_ms: number | null;
  difference_without_hops_ms: number | null;
  verdict: string;
};

/**
 * One finished run: when, how many rounds, the stores and their hop times, every row, and the one-sentence finding.
 * Sent by GET /api/admin/proof, inside the status.
 */
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

/**
 * Whether a run is idle, running, done or failed, and the last result, or null before the first one.
 * Sent by GET /api/admin/proof.
 */
export type Proof = { status: 'idle' | 'running' | 'done' | 'failed'; result: ProofResult | null };
