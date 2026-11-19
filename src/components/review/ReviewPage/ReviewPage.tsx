/**
 * Does:      Draws the review route at ?view=review: first the reason (ADR: The review route gives a reason before it
 *            asks), the first screen the address names (the verdict, or the mirror for ?for=ai) with its proof, its one
 *            primary move and the people who vouch for him; then, under "The full
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
import { MIRROR, type ReviewScreen } from '../../../lib/reviewMirror';
import { REVIEW_STOPS } from '../../../lib/reviewStops';
import { sidebarRestLine, TOUR_ID, VERDICT } from '../../../lib/reviewVerdict';
import { gateFacts } from '../../layout/StoreBar/gateFacts';
import { ReviewFinish } from '../ReviewFinish';
import { ReviewMirror } from '../ReviewMirror';
import { WhereNext } from '../../shared/WhereNext';
import { ReviewProgress } from '../ReviewProgress';
import { ReviewStop, type StopState } from '../ReviewStop';
import { ReviewVerdict } from '../ReviewVerdict';
import styles from './ReviewPage.module.css';

/** What the build carries about this version, read once: gated, the quality numbers held and CodeRabbit's read. */
const GATE = gateFacts();
const QUALITY = GATE.quality === null ? null : { held: GATE.quality.held, of: GATE.quality.of };

export function ReviewPage({
  screen,
  onOpenDoc,
}: {
  /** The first screen: the verdict, or the mirror for a link sent with for=ai. */
  screen: ReviewScreen;
  onOpenDoc: (key: DocKey) => void;
}) {
  const review = useReviewProgress(screen);
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
      {screen === 'mirror' ? (
        <ReviewMirror
          summary={readings.summary}
          recommendations={readings.recommendations}
          gate={{ gated: GATE.gated, version: GATE.version, quality: QUALITY, review: GATE.review }}
          onAction={review.action}
          onOpenDoc={onOpenDoc}
        />
      ) : (
        <ReviewVerdict
          summary={readings.summary}
          recommendations={readings.recommendations}
          quality={QUALITY}
          onAction={review.action}
          onOpenDoc={onOpenDoc}
        />
      )}
      <p className={styles.fold} data-testid="review-fold">
        <span>
          {screen === 'mirror' ? MIRROR.foldAsk : VERDICT.foldAsk}{' '}
          <a href={`#${TOUR_ID}`} onClick={() => review.action('tour')} data-action="review.tour">
            {screen === 'mirror' ? MIRROR.foldLink : VERDICT.foldLink}
          </a>
          {screen === 'mirror' && MIRROR.foldTail}
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
