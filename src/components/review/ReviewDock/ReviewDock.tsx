/**
 * Does:      Draws the phone's one next step on the review route (ADR: The review route gives a reason before it asks,
 *            the addendum on no dead ends), as the whole-page picture draws it: Book 20 minutes and Resume (PDF) fixed
 *            to the foot of a window under 640, off at load, on once the first screen's buttons have scrolled up out
 *            of the window and off again when they return; never drawn while the booking sheet is open. Its buttons
 *            are the first screen's own: the same addresses and the same actions.
 * Does not:  Draw at all from 640 up (its sheet hides it) or open the sheet itself (the page does, on Book).
 * Used by:   ReviewPage.tsx.
 */
import { useEffect, useState } from 'react';
import { dockOn } from '../../../lib/reviewBook';
import type { ReviewAction } from '../../../lib/reviewProgress';
import { BOOK_ADDRESS, RESUME_ADDRESS, VERDICT } from '../../../lib/reviewVerdict';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewDock.module.css';

export function ReviewDock({
  watch,
  hidden,
  onBook,
  onAction,
}: {
  /** The selector of the first screen's button the dock watches. */
  watch: string;
  /** The booking sheet is open: the dock is not drawn. */
  hidden: boolean;
  onBook: () => void;
  onAction: (action: ReviewAction) => void;
}) {
  const [on, setOn] = useState(false);
  useEffect(() => {
    const row = document.querySelector(watch);
    if (!row || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(([entry]) =>
      setOn(!entry.isIntersecting && dockOn(entry.boundingClientRect))
    );
    observer.observe(row);
    return () => observer.disconnect();
  }, [watch]);

  if (hidden) return null;
  return (
    <div
      className={styles.dock}
      data-on={on}
      inert={!on}
      aria-label="Next step"
      role="region"
      data-testid="review-dock"
    >
      <a
        className={`${pills.primary} ${styles.pill}`}
        href={BOOK_ADDRESS}
        onClick={() => {
          onAction('book');
          onBook();
        }}
        data-action="review.book"
      >
        {VERDICT.book}
      </a>
      <a
        className={`${pills.secondary} ${styles.pill}`}
        href={RESUME_ADDRESS}
        onClick={() => onAction('resume')}
        data-action="review.resume"
      >
        {VERDICT.resume}
      </a>
    </div>
  );
}
