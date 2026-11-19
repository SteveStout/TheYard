/**
 * Does:      Draws the short path's second tile: this version, the gate's six parts as the path words them, ticked
 *            only when the build carries the gate's own reading of this version, and the tests passed and failed.
 * Does not:  Read anything (the path hands it the totals and the build's facts) or print the results file's suites.
 * Used by:   ReviewPath.tsx.
 */
import { PATH_GATE_PARTS } from '../../../lib/reviewPath';
import { reviewedWord } from '../../../lib/reviewMirror';
import styles from '../ReviewPath/ReviewPath.module.css';

export function GateTile({
  label,
  gated,
  totals,
  review,
}: {
  label: string;
  gated: boolean;
  totals: { passed: string; failed: string; version: string };
  review: { reviewed: string } | null;
}) {
  return (
    <div className={styles.reading} data-testid="review-path-gate-tile">
      <p className={styles.tileLabel}>
        <span>
          {label} {totals.version}
        </span>
        <span className={styles.live}>Live</span>
      </p>
      <ul className={styles.parts} data-gated={gated}>
        {PATH_GATE_PARTS.map((part, index) => (
          <li key={part} className={styles.part} data-ticked={gated}>
            {part}
            {index === PATH_GATE_PARTS.length - 1 ? ` ${reviewedWord(review)}` : ''}
          </li>
        ))}
      </ul>
      <p className={styles.tileLine}>
        {totals.passed} passed, {totals.failed} failed
      </p>
    </div>
  );
}
