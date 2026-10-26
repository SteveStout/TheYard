/**
 * The Foreman's page at rung 0, as its card draws it: what happened, the
 * readings, the likely cause, where to start and what it could not read. Types
 * only. Its own file because the card has its own endpoint.
 */

/** One card the Foreman read. Sent by GET /api/admin/foreman. */
export type ForemanCardReading = {
  key: string;
  title: string;
  tone: 'fine' | 'amber' | 'red' | 'unread';
  headline: string | null;
  note: string | null;
};

/** Where to start on a card that moved. Sent by GET /api/admin/foreman. */
export type ForemanStart = { card: string; record: string; file: string; test: string };

/** The page. Sent by GET /api/admin/foreman. */
export type ForemanPage = {
  as_of_ms: number;
  version: string;
  what: string;
  readings: ForemanCardReading[];
  cause: string;
  start: ForemanStart[];
  could_not_read: string[];
  markdown: string;
};
