/**
 * Does:      Draws the review route's tick: a white tick on a deep teal disc, the picture's .check, for a gate part that
 *            passed, the quality numbers held and a tour stop reached.
 *            A gate part's tick is numbered, so it fills in its turn when its card is seen. A part that did not pass in
 *            full (CodeRabbit held off by its rate limit) draws a dash in an outlined disc instead, never the tick.
 * Does not:  Say anything to a screen reader: the words beside it do, or move on its own (sales.css starts it).
 * Used by:   GateTile.tsx, QualityTrendTile.tsx, ReviewTour.tsx.
 */
import type { CSSProperties } from 'react';
import styles from './TickDisc.module.css';

/** How long after its card is seen the first tick lands, and the time between ticks (the sales1120 picture). */
const FIRST_S = 0.3;
const APART_S = 0.38;

export function TickDisc({ check, dash = false }: { check?: number; dash?: boolean }) {
  // A gate part's tick is one of the six checks that fill in order while its card is seen (sales.css, op-check).
  const drawn =
    check === undefined
      ? {}
      : {
          'data-mock': 'anim-check',
          style: { '--d': `${(FIRST_S + check * APART_S).toFixed(2)}s` } as CSSProperties,
        };
  return (
    <span
      className={dash ? `${styles.disc} ${styles.dash}` : styles.disc}
      aria-hidden="true"
      {...drawn}
    >
      <svg viewBox="0 0 10 10" width="10" height="10" focusable="false">
        <path
          d={dash ? 'M2.5 5h5' : 'M1.8 5.2l2.1 2.1 4.3-4.6'}
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
