/**
 * The SQL this application ran (ADR: What the database is actually doing), now or over a
 * kept window, on a container that runs a relational store.
 */
import { type CardWindow, stampFor } from '../../lib/keptCards';
import styles from '../AdminPanel.module.css';
import type { SqlStatement, Metrics, StoreLog } from './types';
import { useRead, useKeptWindow, failed, Absent, About, describeParameters } from './common';
import { type Column, DataTable } from './DataTable';

/** A statement: when, how long, which request caused it, the text and what it was handed. */
const sqlColumns = (window_: CardWindow): Column<SqlStatement>[] => [
  { name: 'At', mono: true, cell: (statement) => stampFor(window_, statement.at) },
  { name: 'Took', mono: true, num: true, cell: (statement) => `${statement.duration_ms} ms` },
  { name: 'Caused by', mono: true, cell: (statement) => statement.request ?? 'startup' },
  {
    name: 'Statement',
    cell: (statement) => (
      <>
        <pre className={styles.sql}>{statement.text}</pre>
        <span className={styles.muted}>{statement.outcome}</span>
      </>
    ),
  },
  { name: 'Parameters', mono: true, cell: (statement) => describeParameters(statement.parameters) },
];

export default function SqlCard({ tick }: { tick: number }) {
  const sql = useRead<SqlStatement[]>('/api/admin/sql', tick);
  const store = useRead<StoreLog>('/api/admin/store', tick);
  const metrics = useRead<Metrics>('/api/admin/metrics', tick);
  const kept = useKeptWindow<SqlStatement>('sql', tick);
  const cardWindows = { sql: kept.window };
  const picker = (_card: string) => kept.picker;
  const sqlRows = kept.window === 'now' ? sql : kept.rows;
  // The SQL card when this container has a relational store, and a sentence when it has none
  // (ADR: One container, both stores): the backends list names every store it runs.
  const shown =
    store === null ||
    store === 'failed' ||
    store.store !== 'Azure Cosmos DB' ||
    (metrics !== null &&
      metrics !== 'failed' &&
      (metrics.backends ?? []).some((backend) => backend.sql !== null));
  if (!shown) {
    return (
      <Absent
        name="The SQL this application ran"
        note="This container runs no relational store, so it ran no SQL."
      />
    );
  }
  return (
    <article className={`${styles.wide} op-glass`} data-testid="sql-card">
      <h2 className={styles.cardTitle}>The SQL this application ran</h2>
      <About>
        Every statement Entity Framework sent, newest first, with the request that caused it and how
        long the database took. Parameters are listed by name, type and size. Their values are not
        here and never were: the type this table is built from has no field to put one in, because
        this page is public and a registration&rsquo;s parameters carry an email address. The
        request is the method and the path, without its query string, for the same reason.
        Statements caused by this page and by the health check are left out, or watching would be
        all there was to see. The buffer holds the last 200 in this container&rsquo;s memory and
        empties on every deploy.
      </About>
      {picker('sql')}
      {sqlRows === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : sqlRows === 'failed' ? (
        failed('the SQL log')
      ) : sqlRows.length === 0 ? (
        cardWindows.sql !== 'now' ? null : (
          <p className={styles.muted}>
            Nothing recorded yet. The catalogue is read once at startup and cached, so an idle
            container runs no SQL at all.
          </p>
        )
      ) : (
        <DataTable
          label="SQL statements this application ran"
          rows={sqlRows.slice(0, cardWindows.sql === 'now' ? 60 : 200)}
          rowKey={(_statement, index) => index}
          columns={sqlColumns(cardWindows.sql)}
        />
      )}
    </article>
  );
}
