/**
 * What the document store ran (ADR: What the store is actually doing), now or over
 * a kept window, on a container that runs a document store.
 */
import type { ReactNode } from 'react';
import { type CardWindow, stampFor } from '../../lib/keptCards';
import { documentStore } from '../../lib/metrics';
import styles from '../AdminPanel.module.css';
import type { Metrics, StoreOperation, StoreLog, Fetched } from './types';
import { useRead, useKeptWindow, Absent, About, describeParameters } from './common';
import { type Column, DataTable } from './DataTable';

// #region store-card
/** An operation: when, how long, what it cost, who caused it, where it went and what it carried. */
const storeColumns = (window_: CardWindow): Column<StoreOperation>[] => [
  { name: 'At', mono: true, cell: (operation) => stampFor(window_, operation.at) },
  { name: 'Took', mono: true, num: true, cell: (operation) => `${operation.duration_ms} ms` },
  { name: 'Charge', mono: true, num: true, cell: (operation) => `${operation.request_charge} RU` },
  { name: 'Caused by', mono: true, cell: (operation) => operation.request ?? 'startup' },
  { name: 'Container', mono: true, cell: (operation) => operation.container },
  { name: 'Kind', cell: (operation) => operation.kind },
  {
    name: 'Partition',
    cell: (operation) =>
      operation.partition +
      (operation.partition.startsWith('cross')
        ? ` (${operation.physical_partitions} physical)`
        : ''),
  },
  {
    name: 'Operation',
    cell: (operation) => (
      <>
        <pre className={styles.sql}>{operation.text}</pre>
        <span className={styles.muted}>{operation.outcome}</span>
      </>
    ),
  },
  { name: 'Parameters', mono: true, cell: (operation) => describeParameters(operation.parameters) },
];

/** The document store's counterpart of the SQL card: every operation with its partition and its charge (ADR: What the store is actually doing). */
function StoreTable({
  log,
  rows,
  window: window_,
  picker,
}: {
  log: StoreLog;
  rows: Fetched<StoreOperation[]>;
  window: CardWindow;
  picker: ReactNode;
}) {
  return (
    <article className={`${styles.wide} op-glass`} data-testid="store-card">
      <h2 className={styles.cardTitle}>What the document store ran</h2>
      <About>
        Every operation this container sent to {log.store}, newest first: the container, whether it
        was a point read, a point write, a query or a batch, whether it was pinned to one partition
        or fanned out across every physical partition, and what it cost in request units beside how
        long it took. Parameters are listed by name, type and size and never by value, and a
        partition is described rather than named, because this page is public and the key of an
        account&rsquo;s partition is the account. Operations caused by this page and by the health
        check are left out. The buffer holds the last 200 in this container&rsquo;s memory and
        empties on every deploy.
      </About>
      {picker}
      {rows === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : rows === 'failed' ? (
        <p className={styles.muted} data-testid="card-failed">
          Could not read the store log on the last try; the next try is in 30 seconds.
        </p>
      ) : rows.length === 0 ? (
        window_ !== 'now' ? null : (
          <p className={styles.muted}>Nothing recorded yet.</p>
        )
      ) : (
        <DataTable
          label="Operations this application sent to the document store"
          rows={rows.slice(0, window_ === 'now' ? 60 : 200)}
          rowKey={(_operation, index) => index}
          columns={storeColumns(window_)}
        />
      )}
    </article>
  );
}
// #endregion store-card

export default function StoreCard({ tick }: { tick: number }) {
  const store = useRead<StoreLog>('/api/admin/store', tick);
  const metrics = useRead<Metrics>('/api/admin/metrics', tick);
  const kept = useKeptWindow<StoreOperation>('store', tick);
  const shown =
    store !== null &&
    store !== 'failed' &&
    (store.store === 'Azure Cosmos DB' ||
      (metrics !== null && metrics !== 'failed' && documentStore(metrics) !== null));
  if (!shown) {
    return (
      <Absent
        name="What the document store ran"
        note={
          store === null
            ? null
            : 'This container runs no document store, so it sent no operations to one.'
        }
      />
    );
  }
  return (
    <StoreTable
      log={store}
      rows={kept.window === 'now' ? store.operations : kept.rows}
      window={kept.window}
      picker={kept.picker}
    />
  );
}
