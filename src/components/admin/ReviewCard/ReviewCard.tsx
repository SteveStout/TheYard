/**
 * Review route, how far they got (ADR: Ten minutes, seven stops, the addendum on the reach rows): of the
 * visitors who opened the review route in the window, how many reached each of the seven stops, drawn as
 * eight bars, and how far each tagged link's visitors got. A code says which link was sent, never who
 * received it, so this card is as public as the rest of the tab and names nobody.
 */
import { useState } from 'react';
import {
  countedLine,
  funnelBars,
  reachedWords,
  REORDERED_LINE,
  REVIEW_WINDOWS,
  tookWords,
  windowLabel,
  type ReviewCodeRow,
  type ReviewFunnel,
  type ReviewWindow,
} from '../../../lib/review';
import styles from '../shared/card.module.css';
import { About, failed, useRead } from '../shared/common';
import { CardState } from '../shared/CardState';
import { type Column, DataTable } from '../DataTable';
import { PathBars } from '../PathBars';
import own from './ReviewCard.module.css';

/** A code's row: the code, how far its visitors got, how long it took, and the last day it was seen. */
const columns: Column<ReviewCodeRow>[] = [
  { name: 'Tagged link', mono: true, short: true, cell: (row) => `r=${row.code}` },
  { name: 'Reached', cell: (row) => reachedWords(row.stop) },
  { name: 'Took', short: true, cell: (row) => tookWords(row.minutes) },
  { name: 'Last seen', mono: true, short: true, cell: (row) => row.last_day },
];

export default function ReviewCard({ tick }: { tick: number }) {
  const [window_, setWindow] = useState<ReviewWindow>('30d');
  const funnel = useRead<ReviewFunnel>(`/api/admin/review?window=${window_}`, tick);
  const bars = funnel === null || funnel === 'failed' ? [] : funnelBars(funnel);
  return (
    <article className={`${styles.wide} op-glass`} data-testid="review-card">
      <h2 className={styles.cardTitle}>Review route, how far they got</h2>
      <About>
        Visitors who opened the review route at /?view=review, and the last stop each one reached,
        each visitor counted once in the window. People only: scanners and the site's own reads are
        left out.
      </About>
      <p
        className={`${styles.statusRow} op-seg op-seg-wrap`}
        role="group"
        aria-label="Window for the review route card"
      >
        {REVIEW_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === window_}
            onClick={() => setWindow(option)}
            data-testid={`review-window-${option}`}
          >
            {windowLabel(option)}
          </button>
        ))}
      </p>
      {funnel === null ? (
        <CardState state="loading" />
      ) : funnel === 'failed' ? (
        failed('the review route')
      ) : !funnel.available ? (
        <p className={styles.muted} data-testid="review-absent">
          {funnel.source}
        </p>
      ) : (
        <>
          <PathBars
            rows={bars.map((bar) => ({
              key: String(bar.place),
              name: bar.name,
              count: bar.count,
              share: bar.share,
              testId: `review-bar-${bar.place}`,
            }))}
            label={`The review route over ${windowLabel(window_)}: ${bars
              .map((bar) => `${bar.name} ${bar.count}`)
              .join(', ')}`}
            tone={own.reach}
          />
          {funnel.by_code.length === 0 ? (
            <CardState state="empty" what="tagged links">
              No visitor came to the route through a tagged link in the window.
            </CardState>
          ) : (
            <DataTable
              label="How far each tagged link got"
              testId="review-codes"
              rows={funnel.by_code}
              rowKey={(row) => row.code}
              columns={columns}
            />
          )}
          <p className={styles.muted} data-testid="review-counted">
            {countedLine(funnel.counted_from)}
          </p>
          <p className={styles.muted} data-testid="review-reordered">
            {REORDERED_LINE}
          </p>
        </>
      )}
      <p className={styles.muted}>A code says which link was sent, never who received it.</p>
    </article>
  );
}
