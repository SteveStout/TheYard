/**
 * The telemetry service's view of the site over its window, as the Telemetry
 * card draws it: totals, the slowest routes, the exceptions and the browser
 * errors. Types only. Its own file because it is one endpoint.
 */

/**
 * The request totals: how many, how many failed, and the median and slow-end times.
 * Sent by GET /api/admin/telemetry, in the summary.
 */
export type TelemetrySummary = {
  total: number;
  failed: number;
  p50_ms: number | null;
  p95_ms: number | null;
};

/**
 * One route, how often it was called, and its average time.
 * Sent by GET /api/admin/telemetry, in the slowest routes.
 */
export type TelemetryRoute = { name: string; calls: number; avg_ms: number | null };

/**
 * One kind of exception: its type, the method that threw it, how often, and when last.
 * Sent by GET /api/admin/telemetry, in the exceptions.
 */
export type TelemetryException = { type: string; method: string; count: number; last_at: string };

/**
 * The errors the browser reported: how many, and when the last one arrived.
 * Sent by GET /api/admin/telemetry, in the browser block.
 */
export type TelemetryBrowser = { count: number; last_at: string };

/**
 * The telemetry answer: whether it is set up and readable, and if so every block above.
 * Sent by GET /api/admin/telemetry.
 */
export type Telemetry = {
  configured: boolean;
  available?: boolean;
  note?: string;
  window?: string;
  summary?: TelemetrySummary;
  slowest?: TelemetryRoute[];
  exceptions?: TelemetryException[];
  browser?: TelemetryBrowser;
  /** The newest request of the last day, or null when there is none. */
  newest_request_at?: string | null;
};
