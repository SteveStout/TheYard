/**
 * Does:      Draws the review route at ?view=review (ADR: Ten minutes, seven stops): the title and lede, where the
 *            reviewer is, the seven stops from the one list in their three states, the finish panel, and the rail.
 * Does not:  Hold the stops (reviewStops.ts does) or the place (useReviewProgress.ts keeps it on the browser); the stops
 *            read nothing from the API, so the server draws them whole, and the rail's quotes arrive in the browser.
 * Used by:   App.tsx.
 */
import { useRef } from 'react';
import { useReviewProgress } from '../../../hooks/useReviewProgress';
import { REVIEW_STOPS } from '../../../lib/reviewStops';
import { ReviewFinish } from '../ReviewFinish';
import { ReviewProgress } from '../ReviewProgress';
import { ReviewRail } from '../ReviewRail';
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
          Check my work in ten minutes
        </h1>
        <p className={styles.lede}>
          Seven stops, ten minutes. By the end you will know how I design, how I test and how I
          decide, without taking my word for any of it. Every stop is the running system, and every
          number on it is read live.
        </p>
      </header>
      <ReviewRail />
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
