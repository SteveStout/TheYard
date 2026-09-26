/**
 * The kept log (ADR: Logs that outlive the container), behind the operator's key.
 */
import { useEffect, useState } from 'react';
import { ACTIVITY_WINDOWS, labelFor, type ActivityWindow } from '../../lib/activity';
import {
  LOG_KINDS,
  countsLine,
  describe as describeEvent,
  groupEventsByDay,
  queryFor,
  toneOf,
  type KeptLogs,
  type LogEvent,
  type LogFilter,
  type LogKind,
} from '../../lib/logs';
import styles from '../AdminPanel.module.css';
import type { Fetched } from './types';
import { About } from './common';
import { type Column, DataTable } from './DataTable';

/** A kept line: when, what kind, on which store, what happened, from where, and its detail. */
const KEPT_COLUMNS: Column<LogEvent>[] = [
  { name: 'When', mono: true, short: true, cell: (e) => new Date(e.at).toLocaleTimeString() },
  { name: 'Kind', cell: (e) => e.kind },
  { name: 'Store', cell: (e) => e.store || '(none)' },
  { name: 'What', mono: true, cell: (e) => describeEvent(e) },
  { name: 'Network', mono: true, cell: (e) => e.network },
  {
    name: 'Detail',
    className: styles.detail,
    cell: (e) => (e.kind === 'request' ? e.trace_id : e.detail || e.category),
  },
];

/**
 * The kept log (ADR: Logs that outlive the container): every request, error
 * and warning, written to the document store off the request path and kept
 * for three years, read back here behind the operator's key. Without the key the
 * card says what it is and shows nothing, which is the same line the visitor
 * table draws and for the same reason.
 */
export default function KeptLogsCard({ adminKey }: { adminKey: string | null }) {
  const [window_, setWindow] = useState<ActivityWindow>('24h');
  const [filter, setFilter] = useState<LogFilter>({ kind: '', status: '', path: '' });
  const [applied, setApplied] = useState<LogFilter>(filter);
  const [logs, setLogs] = useState<Fetched<KeptLogs>>(null);
  const key = adminKey;

  useEffect(() => {
    if (key === null) return;
    let live = true;
    void fetch(`/api/admin/logs/kept?${queryFor(window_, applied)}`, {
      headers: { 'X-Admin-Key': key },
    })
      .then((r) =>
        r.ok ? (r.json() as Promise<KeptLogs>) : Promise.reject(new Error(String(r.status)))
      )
      .then((v) => {
        if (live) setLogs(v);
      })
      .catch(() => {
        if (live) setLogs('failed');
      });
    return () => {
      live = false;
    };
  }, [window_, applied, key]);

  return (
    <article className={`${styles.wide} op-glass`} data-testid="kept-logs-card">
      <h2 className={styles.cardTitle}>Kept log</h2>
      <About>
        Every request, every error and every warning, written to Azure Cosmos DB off the request
        path in batches and kept for three years, so the log outlives the container and the thirty
        days Application Insights keeps. The same rule as the visitor table: a request is a token
        that changes daily and a network to three octets, an error is its type, its message and a
        bounded stack, and no field can carry an at sign. Behind a key only the operator holds.
      </About>
      {key === null ? (
        <p className={styles.muted} data-testid="kept-logs-keyless">
          The kept log answers only to the operator's key.
        </p>
      ) : (
        <>
          <p className={`${styles.statusRow} op-seg`} role="group" aria-label="Window">
            {ACTIVITY_WINDOWS.map((option) => (
              <button
                key={option}
                type="button"
                className={styles.back}
                aria-pressed={option === window_}
                onClick={() => {
                  if (option === window_) return;
                  setWindow(option);
                  setLogs(null);
                }}
                data-testid={`kept-logs-window-${option}`}
              >
                {option === '24h'
                  ? 'Last 24 hours'
                  : option === '7d'
                    ? 'Last 7 days'
                    : 'Last 30 days'}
              </button>
            ))}
          </p>
          <form
            className={styles.filterRow}
            aria-label="Narrow the kept log"
            onSubmit={(event) => {
              event.preventDefault();
              setApplied(filter);
              setLogs(null);
            }}
          >
            <label>
              Kind{' '}
              <select
                value={filter.kind}
                onChange={(event) =>
                  setFilter({ ...filter, kind: event.target.value as LogKind | '' })
                }
                data-testid="kept-logs-kind"
              >
                <option value="">any</option>
                {LOG_KINDS.map((kind) => (
                  <option key={kind} value={kind}>
                    {kind}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Status{' '}
              <input
                inputMode="numeric"
                placeholder="any"
                value={filter.status}
                onChange={(event) => setFilter({ ...filter, status: event.target.value })}
                data-testid="kept-logs-status"
              />
            </label>
            <label>
              Path contains{' '}
              <input
                placeholder="any"
                value={filter.path}
                onChange={(event) => setFilter({ ...filter, path: event.target.value })}
                data-testid="kept-logs-path"
              />
            </label>
            <button type="submit" className={styles.back} data-testid="kept-logs-apply">
              Apply
            </button>
          </form>
          {logs === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : logs === 'failed' ? (
            <p className={styles.muted} data-testid="kept-logs-refused">
              The kept log did not answer to this key.
            </p>
          ) : (
            <>
              <p className={styles.muted} data-testid="kept-logs-summary">
                {logs.kept.available
                  ? `${logs.kept.reason}: ${countsLine(logs.counts)} in the window, ${logs.count} shown`
                  : `Nothing is kept on this container: ${logs.kept.reason}.`}{' '}
                Collector: {logs.collector.written} written, {logs.collector.failed_batches} failed
                batches, every {logs.collector.interval_seconds} seconds.
              </p>
              <DataTable
                label="Kept log in the window"
                testId="kept-logs"
                groups={groupEventsByDay(logs.events).map((group) => ({
                  key: group.day,
                  testId: 'kept-logs-day',
                  title: `${labelFor(group.day, '30d')} (${group.day}): ${group.events.length} line${group.events.length === 1 ? '' : 's'}`,
                  rows: group.events,
                }))}
                rowKey={(e, index) => `${e.at}:${e.trace_id}:${index}`}
                rowProps={(e) => {
                  const tone = toneOf(e);
                  return {
                    className:
                      tone === 'error'
                        ? styles.errorLine
                        : tone === 'warn'
                          ? styles.warnLine
                          : undefined,
                  };
                }}
                empty="Nothing in this window."
                columns={KEPT_COLUMNS}
              />
            </>
          )}
        </>
      )}
    </article>
  );
}
