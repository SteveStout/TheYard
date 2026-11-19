/**
 * Does:      Draws the review route at ?view=review: first the reason (ADR: The review route gives a reason before it
 *            asks), the first screen with its proof, its one primary move and one named quote; then, under "The full
 *            tour, if you want it", the seven stops of ADR: Ten minutes, seven stops as they were, where the reviewer
 *            is, the finish panel, and what the sidebar still holds.
 * Does not:  Hold the stops (reviewStops.ts does), the words (reviewVerdict.ts does) or the place (useReviewProgress.ts
 *            keeps it on the browser); the first screen's two answers are read by useReviewReadings.ts.
 * Used by:   App.tsx.
 */
import { useRef } from 'react';
import { useReviewProgress } from '../../../hooks/useReviewProgress';
import { useReviewReadings } from '../../../hooks/useReviewReadings';
import type { DocKey } from '../../../library/documents';
import { DIAGRAMS, MENUS } from '../../../library/sections';
import { REVIEW_STOPS } from '../../../lib/reviewStops';
import { sidebarRestLine, TOUR_ID, VERDICT } from '../../../lib/reviewVerdict';
import { gateFacts } from '../../layout/StoreBar/gateFacts';
import { ReviewFinish } from '../ReviewFinish';
import { WhereNext } from '../../shared/WhereNext';
import { ReviewProgress } from '../ReviewProgress';
import { ReviewStop, type StopState } from '../ReviewStop';
import { ReviewVerdict } from '../ReviewVerdict';
import styles from './ReviewPage.module.css';

/** The quality numbers this build held, read once from what the build carries. */
const QUALITY = gateFacts().quality;

export function ReviewPage({ onOpenDoc }: { onOpenDoc: (key: DocKey) => void }) {
  const review = useReviewProgress();
  const readings = useReviewReadings();
  const { current, reached } = review.progress;
  const finish = useRef<HTMLElement>(null);

  // Once the route is finished no stop is current: every stop reached reads as done.
  const stateOf = (index: number): StopState =>
    index === current && !review.finished ? 'current' : reached.includes(index) ? 'done' : 'next';

  const markDone = () => {
    const last = current === REVIEW_STOPS.length - 1;
    review.markDone();
    // The last stop's Mark done ends the route: the finish panel is where the reader goes next.
    if (last) {
      window.requestAnimationFrame(() => {
        finish.current?.focus({ preventScroll: true });
        finish.current?.scrollIntoView({ block: 'center' });
      });
    }
  };

  return (
    <section className={styles.page} aria-labelledby="review-title" data-testid="review-page">
      <ReviewVerdict
        summary={readings.summary}
        recommendations={readings.recommendations}
        quality={QUALITY === null ? null : { held: QUALITY.held, of: QUALITY.of }}
        onAction={review.action}
        onOpenDoc={onOpenDoc}
      />
      <p className={styles.fold} data-testid="review-fold">
        <span>
          {VERDICT.foldAsk}{' '}
          <a href={`#${TOUR_ID}`} onClick={() => review.action('tour')} data-action="review.tour">
            {VERDICT.foldLink}
          </a>
        </span>
      </p>
      <section
        id={TOUR_ID}
        className={styles.tour}
        aria-labelledby="review-tour-title"
        data-testid="review-tour"
      >
        <header className={styles.head}>
          <h2 id="review-tour-title" className={styles.tourTitle}>
            {VERDICT.tourTitle}
          </h2>
          <p className={styles.lede}>{VERDICT.tourLine}</p>
        </header>
        <ReviewProgress
          current={current}
          reached={reached.length}
          onPrevious={review.previous}
          onNext={review.next}
        />
        <ol className={styles.stops} aria-label="The seven stops">
          {REVIEW_STOPS.map((stop, index) => (
            <ReviewStop
              key={stop.key}
              stop={stop}
              index={index}
              state={stateOf(index)}
              onChoose={() => review.choose(index)}
              onOpened={() => review.opened(index)}
              onMarkDone={markDone}
            />
          ))}
        </ol>
        <p className={styles.lede} data-testid="review-sidebar-rest">
          {sidebarRestLine(MENUS.records.items.length, DIAGRAMS.length)}
        </p>
        <ReviewFinish ref={finish} finished={review.finished} onRestart={review.restart} />
      </section>
      <WhereNext page="review" />
    </section>
  );
}
