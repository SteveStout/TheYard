/**
 * Does:      Draws the panel at the route's end: that was the site, what the sidebar still holds with its two counts read
 *            from the catalogue, and the way back to the first screen or round the route again.
 * Does not:  Type a count (the record and diagram lists are the sidebar's own) or keep the place.
 * Used by:   ReviewPage.tsx.
 */
import { forwardRef } from 'react';
import { DIAGRAMS, MENUS } from '../../../library/sections';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewFinish.module.css';

export const ReviewFinish = forwardRef<
  HTMLElement,
  {
    /** Every stop is done. */
    finished: boolean;
    /** Back to the first stop of the route. */
    onRestart: () => void;
  }
>(function ReviewFinish({ finished, onRestart }, ref) {
  return (
    <section
      ref={ref}
      tabIndex={-1}
      className={`${styles.finish} op-glass op-tile`}
      aria-labelledby="review-finish"
      data-testid="review-finish"
      data-state={finished ? 'finished' : 'open'}
    >
      <h2 id="review-finish" className={styles.heading}>
        That is the site
      </h2>
      <p className={styles.words}>
        Seven stops, about ten minutes, every one of them live. The sidebar holds the rest:{' '}
        {MENUS.records.items.length} decision records, {DIAGRAMS.length} diagrams, the incidents and
        the changelog, for whoever wants to keep going.
      </p>
      <div className={styles.actions}>
        <a className={pills.primary} href="/">
          Back to the first screen
        </a>
        <button type="button" className={pills.secondary} onClick={onRestart}>
          Walk it again
        </button>
      </div>
    </section>
  );
});
