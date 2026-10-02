/**
 * The health read the Admin tab's Health card draws: the overall word, the
 * build, and each check the server ran. Types only, so the card's chunk carries
 * nothing for them. Its own file because it is the one endpoint outside /api/admin.
 */
import type { KeptWarm } from '../../../../lib/keepWarm';

/**
 * One check the health read ran (the database, the store, the dataset), with its word and how long it took.
 * Sent by GET /api/health.
 */
export type HealthCheck = { name: string; status: string; detail: string; duration_ms: number };

/**
 * The whole health answer: the overall word, how long the process has been up, the build, and every check.
 * Sent by GET /api/health.
 */
export type Health = {
  status: string;
  uptime_seconds: number;
  version: string;
  commit: string;
  checks: HealthCheck[];
  /** The keep-warm loop's last pass (ADR: Kept awake); null where it is off. */
  kept_warm?: KeptWarm;
};
