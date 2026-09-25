/**
 * Recent errors, from the server and the browser (ADR-023), now or over a kept
 * window. The strip reads the ring for its tile, so the ring is handed in.
 */
import { type CardWindow, stampFor } from '../../lib/keptCards';
import styles from '../AdminPanel.module.css';
import type { ErrorEntry, Fetched } from './types';
import { useKeptWindow, failed } from './common';
import { type Column, DataTable } from './DataTable';

/** An error: when, what answered, where, what it was, and the stack behind a fold. */
const errorColumns = (window_: CardWindow): Column<ErrorEntry>[] => [
  { name: 'At', mono: true, cell: (entry) => stampFor(window_, entry.at) },
  { name: 'Status', mono: true, cell: (entry) => (entry.status === 0 ? 'browser' : entry.status) },
  { name: 'Where', mono: true, cell: (entry) => entry.path },
  { name: 'What', cell: (entry) => entry.message },
  {
    name: 'Stack',
    cell: (entry, index) =>
      entry.frames.length === 0 ? (
        <span className={styles.muted}>no stack</span>
      ) : (
        <details data-testid={`error-frames-${index}`}>
          <summary>{entry.frames.length} frames</summary>
          <pre className={styles.sql}>{entry.frames.join('\n')}</pre>
        </details>
      ),
  },
];

export default function ErrorsCard({
  errors,
  tick,
}: {
  errors: Fetched<ErrorEntry[]>;
  tick: number;
}) {
  const kept = useKeptWindow<ErrorEntry>('errors', tick);
  const cardWindows = { errors: kept.window };
  const picker = (_card: string) => kept.picker;
  const errorRows = kept.window === 'now' ? errors : kept.rows;
  return (
    <article className={`${styles.card} ${styles.wideCard} op-glass`} data-testid="errors-card">
      <h2 className={styles.cardTitle}>Recent errors</h2>
      {picker('errors')}
      {errorRows === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : errorRows === 'failed' ? (
        failed('the error list')
      ) : errorRows.length === 0 ? (
        cardWindows.errors !== 'now' ? null : (
          <p className={styles.muted}>
            None recorded since the container started, from the server or the browser. The buffer
            holds the last 50 and resets on every deploy; Application Insights keeps the durable
            copy (ADR: Telemetry). A server error carries its stack, file and line beside it; the
            exception&rsquo;s message is deliberately not here, because a message is where a
            framework writes a connection detail and this page is public.
          </p>
        )
      ) : (
        <DataTable
          label="Recent errors"
          testId="errors-table"
          rows={errorRows}
          rowKey={(_entry, index) => index}
          columns={errorColumns(cardWindows.errors)}
        />
      )}
    </article>
  );
}
