/**
 * The page sweep the Admin tab's Pages card draws: every address this container
 * serves, as the last sweep found it. Types only. Its own file because the sweep
 * is one endpoint with its own report shape.
 */

/**
 * One address the sweep checked: what it is, the status and time it answered with, and why it failed if it did.
 * Sent by GET /api/admin/pages, inside the report.
 */
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

/**
 * One finished sweep: what started it, the build it ran on, how many addresses answered, and every entry.
 * Sent by GET /api/admin/pages, inside the status.
 */
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

/**
 * Whether a sweep is running, and the last finished report, or null before the first one.
 * Sent by GET /api/admin/pages.
 */
export type PageStatus = { status: string; report: PageReport | null };
