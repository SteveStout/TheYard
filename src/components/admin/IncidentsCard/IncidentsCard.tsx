/**
 * What broke (ADR: A red that lasts an hour gets a postmortem): every incident the
 * docs keep, each opening its postmortem, and the exceptions and 5xx answers
 * Application Insights kept over seven days, by type and where, never by message.
 * Nothing here is typed by hand: the incidents are the index's rows, and the
 * failures are the workspace's answer, read at most every five minutes.
 */
import { CardState } from '../shared/CardState';
import styles from '../shared/card.module.css';
import type { FailureRow, IncidentRow, IncidentsReport } from '../shared/types';
import { useRead, failed, tonePill } from '../shared/common';
import { type Column, DataTable } from '../DataTable';

/** A failure's newest time in the reader's own clock, with the day when it is not today. */
const when = (ms: number) => {
  const at = new Date(ms);
  if (Number.isNaN(at.getTime()) || ms === 0) return '';
  const time = at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  return at.toDateString() === new Date().toDateString()
    ? time
    : `${at.toLocaleDateString([], { day: 'numeric', month: 'short' })} ${time}`;
};

const incidentColumns: Column<IncidentRow>[] = [
  {
    name: 'Incident',
    rowHeader: true,
    cell: (row) => (
      <a href={`/?doc=${encodeURIComponent(row.slug)}`} data-testid={`incident-${row.id}`}>
        {row.id}: {row.title}
      </a>
    ),
  },
  { name: 'When', cell: (row) => row.dates },
  { name: 'Hours red', cell: (row) => row.hours_red },
  { name: 'Fixed in', mono: true, cell: (row) => row.fixed_in },
];

const failureColumns: Column<FailureRow>[] = [
  { name: 'Newest', mono: true, short: true, cell: (row) => when(row.last_ms) },
  { name: 'App', mono: true, cell: (row) => row.role },
  {
    name: 'What',
    mono: true,
    cell: (row) => `${row.kind === 'answer' ? 'answered ' : ''}${row.what}`,
  },
  { name: 'Where', mono: true, cell: (row) => row.where },
  { name: 'Times', num: true, cell: (row) => row.count },
];

const toneOf = (tone: IncidentsReport['tone']) =>
  tone === 'red' ? 'bad' : tone === 'amber' ? 'warn' : 'good';

export default function IncidentsCard({ tick }: { tick: number }) {
  const report = useRead<IncidentsReport>('/api/admin/incidents', tick);
  return (
    <article className={`${styles.card} op-glass`} data-testid="incidents-card">
      <h2 className={styles.cardTitle}>Incidents, and what broke this week</h2>
      {report === null ? (
        <CardState state="loading" />
      ) : report === 'failed' ? (
        failed('the incidents')
      ) : (
        <>
          <p className={styles.muted}>
            Every red that lasted an hour, and every day that cost twice the week before it, has a
            postmortem; the failures below are what Application Insights kept from every app in the
            last seven days, an exception by its type and an answer of 500 or more by its status.
          </p>
          <DataTable
            label="Incidents"
            testId="incidents-table"
            rows={report.incidents}
            rowKey={(row) => row.id}
            columns={incidentColumns}
            empty="none yet"
          />
          <div className={styles.statusRow}>
            {report.failures_available ? (
              <span className={tonePill(toneOf(report.tone))} data-testid="incidents-headline">
                {report.headline}
              </span>
            ) : (
              <span className={styles.muted} data-testid="incidents-note">
                {report.failures_note}
              </span>
            )}
          </div>
          {report.failures_available ? (
            <DataTable
              label="Failures in the last seven days"
              testId="failures-table"
              rows={report.failures}
              rowKey={(row, index) => `${row.role}|${row.kind}|${row.what}|${row.where}|${index}`}
              columns={failureColumns}
              empty="none in seven days"
            />
          ) : null}
        </>
      )}
    </article>
  );
}
