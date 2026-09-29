/**
 * Recent errors, from the server and the browser (ADR-023), now or over a kept
 * window. The strip reads the ring for its tile, so the ring is handed in.
 */
import { type CardWindow, stampFor } from '../../../lib/keptCards';
import styles from './ErrorsCard.module.css';
import cardStyles from '../shared/card.module.css';
import type { ErrorEntry, Fetched } from '../shared/types';
import { useKeptWindow, failed } from '../shared/common';
import { type Column, DataTable } from '../DataTable';
import { messageParts } from '../../../lib/errorText';

/** An error: when, what answered, where, what it was, and the stack behind a fold. */
const errorColumns = (window_: CardWindow): Column<ErrorEntry>[] => [
  { name: 'At', mono: true, short: true, cell: (entry) => stampFor(window_, entry.at) },
  {
    name: 'Status',
    mono: true,
    short: true,
    cell: (entry) => (entry.status === 0 ? 'browser' : entry.status),
  },
  { name: 'Where', mono: true, cell: (entry) => entry.path },
  {
    name: 'What',
    // An address or a path in the message is an identifier and may break anywhere; the words stay words.
    cell: (entry) =>
      messageParts(entry.message).map((part, index) =>
        part.identifier ? (
          <code key={index} className={styles.codeToken}>
            {part.text}
          </code>
        ) : (
          <span key={index}>{part.text}</span>
        )
      ),
  },
  {
    name: 'Stack',
    cell: (entry, index) =>
      entry.frames.length === 0 ? (
        <span className={cardStyles.muted}>no stack</span>
      ) : (
        <details data-testid={`error-frames-${index}`}>
          <summary>{entry.frames.length} frames</summary>
          <pre className={cardStyles.sql}>{entry.frames.join('\n')}</pre>
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
    <article className={`${cardStyles.card} ${styles.wideCard} op-glass`} data-testid="errors-card">
      <h2 className={cardStyles.cardTitle}>Recent errors</h2>
      {picker('errors')}
      {errorRows === null ? (
        <p className={cardStyles.muted}>Loading…</p>
      ) : errorRows === 'failed' ? (
        failed('the error list')
      ) : errorRows.length === 0 ? (
        cardWindows.errors !== 'now' ? null : (
          <p className={cardStyles.muted}>
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
