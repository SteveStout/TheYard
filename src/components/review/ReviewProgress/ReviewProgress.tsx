/**
 * Does:      Draws where the reviewer is on the route: the ring of stops reached, the stop showing, the minutes left,
 *            Previous and Next stop, and the line that says the place is kept on this browser.
 * Does not:  Keep the place (useReviewProgress.ts does) or know what a stop holds (reviewStops.ts does).
 * Used by:   ReviewPage.tsx.
 */
import { minutesLeft, REVIEW_STOPS } from '../../../lib/reviewStops';
import { Ring } from '../../shared/Ring';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewProgress.module.css';

export function ReviewProgress({
  current,
  reached,
  onPrevious,
  onNext,
}: {
  /** The stop showing "Do this", its index from 0. */
  current: number;
  /** How many stops are done. */
  reached: number;
  onPrevious: () => void;
  onNext: () => void;
}) {
  const of = REVIEW_STOPS.length;
  const left = minutesLeft(current);
  return (
    <section
      className={`${styles.where} op-glass op-tile`}
      aria-labelledby="review-where"
      data-testid="review-progress"
      data-current={current + 1}
      data-reached={reached}
    >
      <span className={styles.ring}>
        <Ring
          value={reached}
          max={of}
          size="page"
          inside={`${reached}/${of}`}
          label={`${reached} of ${of} stops done`}
          testId="review-ring"
        />
      </span>
      <div className={styles.words}>
        <p id="review-where" className={styles.eyebrow}>
          Where you are
        </p>
        <p className={styles.stop}>
          Stop {current + 1} of {of}
        </p>
        <p className={styles.left}>
          About {left} {left === 1 ? 'minute' : 'minutes'} left
        </p>
        <div className={styles.moves}>
          <button
            type="button"
            className={pills.secondary}
            onClick={onPrevious}
            disabled={current === 0}
          >
            Previous
          </button>
          <button
            type="button"
            className={pills.primary}
            onClick={onNext}
            disabled={current === of - 1}
          >
            Next stop
          </button>
        </div>
      </div>
      <p className={styles.kept}>
        Your place is kept on this browser. Leave and come back; it picks up where you were.
      </p>
    </section>
  );
}
