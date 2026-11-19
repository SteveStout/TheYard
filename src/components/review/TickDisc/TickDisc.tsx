/**
 * Does:      Draws the review route's tick: a white tick on a deep teal disc, the picture's .check, for a gate part that
 *            passed, the quality numbers held and a tour stop reached.
 * Does not:  Say anything to a screen reader: the words beside it do.
 * Used by:   GateTile.tsx, QualityTrendTile.tsx, ReviewTour.tsx.
 */
import styles from './TickDisc.module.css';

export function TickDisc() {
  return (
    <span className={styles.disc} aria-hidden="true">
      <svg viewBox="0 0 10 10" width="10" height="10" focusable="false">
        <path
          d="M1.8 5.2l2.1 2.1 4.3-4.6"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    </span>
  );
}
