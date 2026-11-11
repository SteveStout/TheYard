/**
 * Does:      Draws Time to first touch (ADR: The Nightly Function on the Admin tab, the addendum on time to first
 *            touch): for each of the last seven Central days, the roles the pounce caught, the ones a first message
 *            reached, the median and worst minutes to that message, and the ones still waiting with the oldest wait,
 *            each day judged by the server against a median of 60 minutes, all read from GET /api/admin/pounce.
 * Does not:  Pick a day's line or judge it (the server does), or show a company, a title or a person: counts only.
 * Used by:   AdminBenchCard.tsx, through cardChunks.ts, on demand.
 */
import {
  anyLine,
  type FirstTouchReport,
  JUDGEMENT_WORDS,
  measureLine,
  minuteCell,
  NO_LINE_YET,
  type PounceDay,
} from '../../../lib/firstTouch';
import styles from '../shared/card.module.css';
import { failed, tonePill, TONE_WORDS, useRead } from '../shared/common';
import { CardState } from '../shared/CardState';
import { type Column, DataTable } from '../DataTable';

/** The server's judgement as the tab's pill, or muted words for a quiet day or one with no line. */
function Judgement({ day }: { day: PounceDay }) {
  const pill =
    day.judgement === 'on_target'
      ? 'good'
      : day.judgement === 'late'
        ? 'bad'
        : day.judgement === 'waiting'
          ? 'warn'
          : null;
  if (pill === null) {
    return <span className={styles.muted}>{JUDGEMENT_WORDS[day.judgement]}</span>;
  }
  return (
    <span className={tonePill(pill)} title={TONE_WORDS[pill]}>
      {JUDGEMENT_WORDS[day.judgement]}
    </span>
  );
}

/** A count, or blank on a day with no line. */
const count = (day: PounceDay, value: number) => (day.read ? value.toLocaleString('en-US') : '');

/** A day's row: the day, its six numbers and its judgement. */
const columns: Column<PounceDay>[] = [
  { name: 'Day', rowHeader: true, mono: true, cell: (day) => day.day },
  { name: 'Caught', mono: true, short: true, cell: (day) => count(day, day.caught) },
  { name: 'Touched', mono: true, short: true, cell: (day) => count(day, day.touched) },
  {
    name: 'Median',
    mono: true,
    short: true,
    cell: (day) => minuteCell(day.touched, day.median_min),
  },
  { name: 'Worst', mono: true, short: true, cell: (day) => minuteCell(day.touched, day.worst_min) },
  { name: 'Waiting', mono: true, short: true, cell: (day) => count(day, day.waiting) },
  {
    name: 'Oldest wait',
    mono: true,
    short: true,
    cell: (day) => minuteCell(day.waiting, day.oldest_wait_min),
  },
  { name: 'Against 60 min', cell: (day) => <Judgement day={day} /> },
];

export default function PounceCard({ tick }: { tick: number }) {
  const report = useRead<FirstTouchReport>('/api/admin/pounce', tick);
  return (
    <article className={`${styles.wide} op-glass`} data-testid="pounce-card">
      <h2 className={styles.cardTitle}>Time to first touch</h2>
      <p className={styles.muted} data-testid="pounce-measure">
        {measureLine(report !== null && report !== 'failed' ? report.target_min : 60)}
      </p>
      {report === null ? (
        <CardState state="loading" />
      ) : report === 'failed' ? (
        failed('time to first touch')
      ) : !report.available ? (
        <p className={styles.muted} data-testid="pounce-absent">
          {report.note ?? report.source}
        </p>
      ) : !anyLine(report) ? (
        <p className={styles.muted} data-testid="pounce-empty">
          {NO_LINE_YET}
        </p>
      ) : (
        <>
          <DataTable
            label="Time to first touch, day by day"
            testId="pounce-days"
            rows={report.days}
            rowKey={(day) => day.day}
            columns={columns}
          />
          <p className={styles.muted} data-testid="pounce-source">
            Each day is its newest line; read from {report.source}
          </p>
        </>
      )}
    </article>
  );
}
