/**
 * Does:      Draws the panel at the route's end: that was the site, what the sidebar still holds with its two counts read
 *            from the catalogue, the way back to the first screen or round the route again, and the two ways to reach
 *            Steven, as links with no pitch (profile1110-f2: a manager who finished the ten minutes had no way to write).
 * Does not:  Type a count (the record and diagram lists are the sidebar's own) or keep the place.
 * Used by:   ReviewPage.tsx.
 */
import { forwardRef } from 'react';
import { DIAGRAMS, MENUS } from '../../../library/sections';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewFinish.module.css';

/** The two ways to reach Steven, the same two the About page closes with. */
export const REACH = [
  { label: 'Email Steven', href: 'mailto:stevenstout11@gmail.com' },
  { label: 'Message me on LinkedIn', href: 'https://www.linkedin.com/in/stevenwstout/' },
] as const;

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
        That is the work
      </h2>
      <p className={styles.words}>
        Seven stops, about ten minutes, every one of them live. The reasoning behind all of it is in
        the sidebar: {MENUS.records.items.length} decision records, {DIAGRAMS.length} diagrams, the
        incidents and the changelog.
      </p>
      <div className={styles.actions}>
        <a className={pills.primary} href="/">
          Back to the first screen
        </a>
        <button type="button" className={pills.secondary} onClick={onRestart}>
          Walk it again
        </button>
      </div>
      <ul className={styles.reach} aria-label="Reach Steven" data-testid="review-reach">
        {REACH.map((way) => (
          <li key={way.label}>
            <a href={way.href} rel="noopener">
              {way.label}
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
});
