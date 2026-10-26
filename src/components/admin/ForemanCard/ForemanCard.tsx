/**
 * The Foreman at rung 0 (ADR: The Foreman, a place to start): one page that reads
 * every Cloud infrastructure card and the failures Application Insights kept, and
 * says what happened, what moved, what is nearest, where to start and what it could
 * not read. No model and no action: every line is computed from the readings.
 */
import { CardState } from '../shared/CardState';
import styles from '../shared/card.module.css';
import type { ForemanCardReading, ForemanPage, ForemanStart } from '../shared/types';
import { useRead, failed, tonePill } from '../shared/common';
import { type Column, DataTable } from '../DataTable';

const toneOf = (tone: ForemanCardReading['tone']) =>
  tone === 'red' ? 'bad' : tone === 'amber' ? 'warn' : 'good';

const readingColumns: Column<ForemanCardReading>[] = [
  {
    name: 'Card',
    rowHeader: true,
    cell: (row) => <a href={`/?view=admin&card=${encodeURIComponent(row.key)}`}>{row.title}</a>,
  },
  {
    name: 'Tone',
    short: true,
    cell: (row) => <span className={tonePill(toneOf(row.tone))}>{row.tone}</span>,
  },
  { name: 'Figure', mono: true, cell: (row) => row.headline ?? '' },
  { name: 'Note', cell: (row) => row.note ?? '' },
];

const startColumns: Column<ForemanStart>[] = [
  { name: 'Card', rowHeader: true, cell: (row) => row.card },
  { name: 'Record', cell: (row) => row.record },
  { name: 'File', mono: true, cell: (row) => row.file },
  { name: 'Test', mono: true, cell: (row) => row.test },
];

export default function ForemanCard({ tick }: { tick: number }) {
  const page = useRead<ForemanPage>('/api/admin/foreman', tick);
  return (
    <article className={`${styles.card} op-glass`} data-testid="foreman-card">
      <h2 className={styles.cardTitle}>The Foreman, a place to start</h2>
      {page === null ? (
        <CardState state="loading" />
      ) : page === 'failed' ? (
        failed('the Foreman')
      ) : (
        <>
          <h3>What happened</h3>
          <p data-testid="foreman-what">{page.what}</p>
          <h3>The readings</h3>
          <DataTable
            label="The cards that moved"
            testId="foreman-readings"
            rows={page.readings}
            rowKey={(row) => row.key}
            columns={readingColumns}
            empty="every card reads fine"
          />
          <h3>The likely cause</h3>
          <p data-testid="foreman-cause">{page.cause}</p>
          <h3>Where to start</h3>
          <DataTable
            label="Where to start"
            testId="foreman-start"
            rows={page.start}
            rowKey={(row) => row.card}
            columns={startColumns}
            empty="nowhere: nothing moved"
          />
          <h3>What it could not read</h3>
          {page.could_not_read.length === 0 ? (
            <p className={styles.muted}>Nothing: every card and Application Insights answered.</p>
          ) : (
            <ul data-testid="foreman-unread">
              {page.could_not_read.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          )}
          <p className={styles.muted}>
            Rung 0: read from {page.version}, nothing acted on and no model. The same page as
            markdown is at <a href="/api/admin/foreman.md">/api/admin/foreman.md</a>.
          </p>
        </>
      )}
    </article>
  );
}
