// The kept log's shapes and the arithmetic the card needs (ADR: Logs that
// outlive the container). The wire shape mirrors LogReport in the API; the
// helpers here turn a list of events into what the table shows and stay
// pure so the tests can hold them without a browser.

import type { ActivityWindow } from './activity';

export type LogKind = 'request' | 'error' | 'app';
export const LOG_KINDS: LogKind[] = ['request', 'error', 'app'];

export type LogEvent = {
  at: string;
  kind: LogKind;
  store: string;
  level: string;
  category: string;
  method: string;
  path: string;
  status: number;
  duration_ms: number;
  visitor: string;
  network: string;
  message: string;
  detail: string;
  trace_id: string;
};

export type KeptLogs = {
  window: ActivityWindow;
  since: string;
  until: string;
  kept: { available: boolean; reason: string };
  counts: { kind: LogKind; count: number }[];
  query: { kind: string; status: number | null; path: string };
  count: number;
  events: LogEvent[];
  collector: {
    offered: number;
    written: number;
    failed_batches: number;
    last_write: string | null;
    interval_seconds: number;
  };
};

/** The filter the card sends: empty strings mean "any". */
export type LogFilter = { kind: LogKind | ''; status: string; path: string };

/** The query string for a filter, with only the parts that narrow anything, and the path a value never syntax. */
export function queryFor(window: ActivityWindow, filter: LogFilter): string {
  const parts = [`window=${window}`];
  if (filter.kind) parts.push(`kind=${filter.kind}`);
  const status = filter.status.trim();
  if (/^\d{3}$/.test(status)) parts.push(`status=${status}`);
  const path = filter.path.trim();
  if (path) parts.push(`path=${encodeURIComponent(path.slice(0, 80))}`);
  return parts.join('&');
}

/** One line for an event: what happened, in the words the kind calls for. */
export function describe(e: LogEvent): string {
  if (e.kind === 'request') {
    return `${e.method} ${e.path} ${e.status} in ${e.duration_ms} ms`;
  }
  const head = e.message || e.category;
  const firstDetailLine = e.detail.split('\n')[0] ?? '';
  return firstDetailLine ? `${head} (${firstDetailLine})` : head;
}

/** The counts as one sentence for the head of the card. */
export function countsLine(counts: KeptLogs['counts']): string {
  const of = (kind: LogKind) => counts.find((c) => c.kind === kind)?.count ?? 0;
  return `${of('request')} requests, ${of('error')} errors, ${of('app')} warnings`;
}

/** Group events by UTC day, newest day first, keeping each day's order as it arrived (newest first). */
export function groupEventsByDay(events: LogEvent[]): { day: string; events: LogEvent[] }[] {
  const groups = new Map<string, LogEvent[]>();
  for (const e of events) {
    const day = e.at.slice(0, 10);
    const list = groups.get(day);
    if (list) list.push(e);
    else groups.set(day, [e]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => (a < b ? 1 : a > b ? -1 : 0))
    .map(([day, list]) => ({ day, events: list }));
}

/** The css class a status earns: nothing under 400, a warning under 500, an error from there. */
export function toneOf(e: LogEvent): 'plain' | 'warn' | 'error' {
  if (e.kind === 'error') return 'error';
  if (e.kind === 'app') return 'warn';
  if (e.status >= 500) return 'error';
  if (e.status >= 400) return 'warn';
  return 'plain';
}
