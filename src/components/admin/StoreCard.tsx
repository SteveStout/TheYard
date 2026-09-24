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

// #region store-card
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
        <div
          className={styles.tableWrap}
          role="region"
          aria-label="Operations this application sent to the document store"
          tabIndex={0}
        >
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">At</th>
                <th scope="col">Took</th>
                <th scope="col">Charge</th>
                <th scope="col">Caused by</th>
                <th scope="col">Container</th>
                <th scope="col">Kind</th>
                <th scope="col">Partition</th>
                <th scope="col">Operation</th>
                <th scope="col">Parameters</th>
              </tr>
            </thead>
            <tbody>
              {rows.slice(0, window_ === 'now' ? 60 : 200).map((operation, index) => (
                <tr key={index}>
                  <td className={styles.mono}>{stampFor(window_, operation.at)}</td>
                  <td className={styles.mono}>{operation.duration_ms} ms</td>
                  <td className={styles.mono}>{operation.request_charge} RU</td>
                  <td className={styles.mono}>{operation.request ?? 'startup'}</td>
                  <td className={styles.mono}>{operation.container}</td>
                  <td>{operation.kind}</td>
                  <td>
                    {operation.partition}
                    {operation.partition.startsWith('cross')
                      ? ` (${operation.physical_partitions} physical)`
                      : ''}
                  </td>
                  <td>
                    <pre className={styles.sql}>{operation.text}</pre>
                    <span className={styles.muted}>{operation.outcome}</span>
                  </td>
                  <td className={styles.mono}>{describeParameters(operation.parameters)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
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
