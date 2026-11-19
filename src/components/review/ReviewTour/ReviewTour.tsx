/**
 * Does:      Draws the review route's full tour as the whole-page picture draws it (ADR: Ten minutes, seven stops, and
 *            the addendum on one unit): "The full tour, if you want it" with "N of 7 seen." and Start over, then eight
 *            compact cells at the page's width. Cells 1 to 7 are the stops, each whole cell a link to its address that
 *            reports the stop reached and turns its ring into a tick; cell 8 is what the sidebar still holds, and opens it.
 * Does not:  Keep the place (useReviewProgress.ts does) or hold a stop's words (reviewStops.ts does).
 * Used by:   ReviewPage.tsx.
 */
import { DIAGRAMS, MENUS } from '../../../library/sections';
import { REVIEW_STOPS, tourLine } from '../../../lib/reviewStops';
import { restLine, TOUR, TOUR_ID, tourSeen } from '../../../lib/reviewVerdict';
import { ReviewHead } from '../ReviewHead';
import { TickDisc } from '../TickDisc';
import styles from './ReviewTour.module.css';

export function ReviewTour({
  reached,
  counts,
  onOpened,
  onStartOver,
  onOpenSidebar,
}: {
  /** The stops reached on this browser, each an index from 0. */
  reached: readonly number[];
  /** The live counts two lines carry, in words, or "" before their answers. */
  counts: { vehicles: string; recommendations: string };
  onOpened: (index: number) => void;
  onStartOver: () => void;
  onOpenSidebar: () => void;
}) {
  return (
    <section id={TOUR_ID} aria-labelledby="review-tour-title" data-testid="review-tour">
      <ReviewHead
        id="review-tour-title"
        title={TOUR.title}
        meta={
          <>
            <strong data-testid="review-tour-seen">
              {tourSeen(reached.length, REVIEW_STOPS.length)}
            </strong>{' '}
            <button type="button" className={styles.startOver} onClick={onStartOver}>
              {TOUR.startOver}
            </button>
          </>
        }
        hint={TOUR.line}
      />
      <ol className={styles.stops} aria-label="The seven stops">
        {REVIEW_STOPS.map((stop, index) => {
          const seen = reached.includes(index);
          return (
            <li key={stop.key}>
              <a
                className={`${styles.stop} op-glass`}
                href={stop.address}
                onClick={() => onOpened(index)}
                data-reached={seen}
                data-testid={`review-tour-${stop.key}`}
              >
                <span className={styles.head}>
                  <span className={styles.ring} aria-hidden="true">
                    {seen ? <TickDisc /> : index + 1}
                  </span>
                  <span className={styles.title}>{stop.title}</span>
                  <span className={styles.minutes}>
                    {stop.minutes} min
                    {seen && <span className={styles.srOnly}>, seen</span>}
                  </span>
                </span>
                <span className={styles.line}>{tourLine(stop, counts)}</span>
              </a>
            </li>
          );
        })}
        <li>
          <button
            type="button"
            className={`${styles.stop} op-glass op-tile`}
            onClick={onOpenSidebar}
            data-testid="review-tour-rest"
          >
            <span className={styles.head}>
              <span className={styles.title}>{TOUR.restTitle}</span>
            </span>
            <span className={styles.line}>
              {restLine(MENUS.records.items.length, DIAGRAMS.length)}
            </span>
          </button>
        </li>
      </ol>
    </section>
  );
}
