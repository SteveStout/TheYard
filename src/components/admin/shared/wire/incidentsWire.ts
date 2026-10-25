/**
 * What broke, as the first card under 05 What broke? lists it: every incident the
 * docs keep, and the failures Application Insights kept over seven days. Types
 * only. Its own file because the card has its own endpoint.
 */

/** One incident as docs/incidents/INCIDENTS.md lists it. Sent by GET /api/admin/incidents. */
export type IncidentRow = {
  id: string;
  title: string;
  dates: string;
  hours_red: string;
  card: string;
  fixed_in: string;
  slug: string;
};

/** One kind of failure: an exception by type, or an answer of 500 or more by status. Sent by GET /api/admin/incidents. */
export type FailureRow = {
  role: string;
  kind: string;
  what: string;
  where: string;
  count: number;
  last_ms: number;
};

/** The card's answer. Sent by GET /api/admin/incidents. */
export type IncidentsReport = {
  as_of_ms: number;
  window: string;
  incidents: IncidentRow[];
  failures_available: boolean;
  failures_note: string | null;
  failures: FailureRow[];
  headline: string;
  tone: 'fine' | 'amber' | 'red' | 'unread';
};
