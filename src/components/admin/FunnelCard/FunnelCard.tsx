/**
 * The job hunt funnel (ADR: The Nightly Function on the Admin tab, the addendum on the funnel): the Function's
 * daily line added up over the window, postings scanned through to calls booked, each step with the share of the
 * step before it, and the newest seven days' calls booked against two to three a week, graded by the server.
 * Every number is read from GET /api/admin/funnel; nothing here is typed in.
 */
import { useState } from 'react';
import {
  daysLine,
  FUNNEL_WINDOWS,
  funnelSteps,
  weekLine,
  type FunnelStep,
  type FunnelWindow,
  type JobFunnel,
} from '../../../lib/jobFunnel';
import styles from '../shared/card.module.css';
import { About, failed, tonePill, TONE_WORDS, useRead } from '../shared/common';
import { CardState } from '../shared/CardState';
import { type Column, DataTable } from '../DataTable';

/** A step's row: its name, its count over the window, and how many of the step before went on to it. */
const columns: Column<FunnelStep>[] = [
  { name: 'Step', rowHeader: true, cell: (step) => step.name },
  { name: 'Count', mono: true, short: true, cell: (step) => step.count.toLocaleString('en-US') },
  {
    name: 'Of the step before',
    mono: true,
    short: true,
    cell: (step) => (step.ofBefore === null ? '' : `${step.ofBefore} %`),
  },
];

/** The server's tone as the tab's pill: fine, worth a look, or needs attention. */
function TonePill({ tone }: { tone: JobFunnel['tone'] }) {
  if (tone === 'unread') {
    return <span className={styles.muted}>not judged: no line this week</span>;
  }
  const pill = tone === 'fine' ? 'good' : tone === 'amber' ? 'warn' : 'bad';
  return <span className={tonePill(pill)}>{TONE_WORDS[pill]}</span>;
}

export default function FunnelCard({ tick }: { tick: number }) {
  const [window_, setWindow] = useState<FunnelWindow>('7d');
  const funnel = useRead<JobFunnel>(`/api/admin/funnel?window=${window_}`, tick);
  return (
    <article className={`${styles.wide} op-glass`} data-testid="funnel-card">
      <h2 className={styles.cardTitle}>The job hunt funnel</h2>
      <About>
        What the job hunt did in the window, from the Function&apos;s daily line: postings the scans
        read, the ones staged READY, applications sent, people reached, replies from people and
        first calls booked. The tone is the last seven days&apos; calls against two to three a week.
      </About>
      <p
        className={`${styles.statusRow} op-seg op-seg-wrap`}
        role="group"
        aria-label="Window for the job hunt funnel card"
      >
        {FUNNEL_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === window_}
            onClick={() => setWindow(option)}
            data-testid={`funnel-window-${option}`}
          >
            {option.replace(/(\d+)([a-z])/, '$1 $2')}
          </button>
        ))}
      </p>
      {funnel === null ? (
        <CardState state="loading" />
      ) : funnel === 'failed' ? (
        failed('the job hunt funnel')
      ) : !funnel.available ? (
        <p className={styles.muted} data-testid="funnel-absent">
          {funnel.note ?? funnel.source}
        </p>
      ) : (
        <>
          <p className={styles.statusRow} data-testid="funnel-week">
            <TonePill tone={funnel.tone} /> {weekLine(funnel)}
          </p>
          <DataTable
            label="The job hunt funnel, step by step"
            testId="funnel-steps"
            rows={funnelSteps(funnel)}
            rowKey={(step) => step.name}
            columns={columns}
          />
          <p className={styles.muted} data-testid="funnel-days">
            {daysLine(funnel.days)}; read from {funnel.source}
          </p>
        </>
      )}
    </article>
  );
}
