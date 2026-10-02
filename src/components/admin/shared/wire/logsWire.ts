/**
 * The three raw lists the Admin tab shows line by line: the SQL statements, the
 * document store's operations and the console log. Types only. One file because
 * the SQL and store lists share the parameter shape and the cards that read them.
 */

/**
 * One parameter's name, type and size; never its value, so a list cannot leak what a visitor typed.
 * Sent by GET /api/admin/sql and GET /api/admin/store, beside each statement.
 */
export type SqlParameterShape = { name: string; type: string; size: number | null };

/**
 * One SQL statement the database ran: its text, parameters, time, outcome and the request that caused it.
 * Sent by GET /api/admin/sql (and by GET /api/admin/kept?card=sql for a kept window).
 */
export type SqlStatement = {
  at: string;
  text: string;
  parameters: SqlParameterShape[];
  duration_ms: number;
  outcome: string;
  request: string | null;
};

/**
 * One console log line: when, its level and category, the message, and the exception if one came with it.
 * Sent by GET /api/admin/logs (and by GET /api/admin/kept?card=logs for a kept window).
 */
export type LogEntry = {
  at: string;
  level: string;
  category: string;
  message: string;
  exception: string | null;
};

/**
 * One document store operation: the container, the query shape, the partition it hit and the charge beside the time.
 * Sent by GET /api/admin/store (and by GET /api/admin/kept?card=store for a kept window).
 */
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

/**
 * The store list: which store answered, and its operations newest first; empty on a relational container.
 * Sent by GET /api/admin/store.
 */
export type StoreLog = { store: string; operations: StoreOperation[] };
