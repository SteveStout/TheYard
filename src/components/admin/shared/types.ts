/**
 * The shapes the Admin tab's endpoints answer with, shared by its cards (ADR:
 * The Admin tab, as a product, the addendum on the workbench). Types only, so a
 * card's chunk that imports them carries nothing for it. One line per endpoint
 * group, with the file beside it that holds the shapes.
 */

export type { HealthCheck, Health } from './wire/healthWire'; // GET /api/health
export type { ErrorEntry } from './wire/errorsWire'; // GET /api/errors
export type { PageEntry, PageReport, PageStatus } from './wire/pagesWire'; // GET /api/admin/pages
export type { MachineSample, ResourceStatRow, DocumentMinute, Machines } from './wire/machinesWire'; // GET /api/admin/machines
export type { AzureEvent, AzureState } from './wire/azureWire'; // GET /api/admin/azure
export type {
  TelemetrySummary,
  TelemetryRoute,
  TelemetryException,
  TelemetryBrowser,
  Telemetry,
} from './wire/telemetryWire'; // GET /api/admin/telemetry
export type {
  SqlParameterShape,
  SqlStatement,
  LogEntry,
  StoreOperation,
  StoreLog,
} from './wire/logsWire'; // GET /api/admin/sql, /api/admin/logs and /api/admin/store
export type {
  EndpointTiming,
  StatusCount,
  RouteTiming,
  StoreSummary,
  RouteCharge,
  Startup,
  SqlSummary,
  BackendMetrics,
  Metrics,
  Peer,
} from './wire/metricsWire'; // GET /api/admin/metrics and /api/admin/peer
export type { ExperimentRow, Experiment } from './wire/experimentWire'; // GET /api/admin/experiment
export type { ProofCell, ProofRow, ProofResult, Proof } from './wire/proofWire'; // GET /api/admin/proof

/**
 * A card's data: nothing yet, the value, or the word that the last fetch failed,
 * so a card can tell "still loading" from "the server did not answer" (ADR-017).
 */
export type Fetched<T> = T | null | 'failed';
