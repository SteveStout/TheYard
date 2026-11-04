/**
 * Does:      Draws the review route at ?view=review (ADR: Ten minutes, seven stops): the title and lede, where the
 *            reviewer is, the seven stops from the one list in their three states, and the finish panel.
 * Does not:  Hold the stops (reviewStops.ts does) or the place (useReviewProgress.ts keeps it on the browser); it reads
 *            nothing from the API, so the server draws it whole.
 * Used by:   App.tsx.
 */
import { useRef } from 'react';
import { useReviewProgress } from '../../../hooks/useReviewProgress';
import { REVIEW_STOPS } from '../../../lib/reviewStops';
import { ReviewFinish } from '../ReviewFinish';
import { ReviewProgress } from '../ReviewProgress';
import { ReviewStop, type StopState } from '../ReviewStop';
import styles from './ReviewPage.module.css';

export function ReviewPage() {
  const review = useReviewProgress();
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
      <header className={styles.head}>
        <p className={styles.eyebrow}>Review</p>
        <h1 id="review-title" className={styles.title}>
          Ten minutes, seven stops
        </h1>
        <p className={styles.lede}>
          The site in the order a reviewer asks about it. Each stop is one live thing to look at and
          one line on what it shows. Nothing here is a pitch: every number is read from the running
          system.
        </p>
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
      <ReviewFinish ref={finish} finished={review.finished} onRestart={review.restart} />
    </section>
  );
}
