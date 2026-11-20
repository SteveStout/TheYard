/**
 * Does:      Draws the short path's second tile as the whole-page picture draws it: "This version, <version>", the gate's
 *            six parts in two columns as the path words them, each ticked only when the build carries the gate's own
 *            reading of this version, then under a rule the tests passed and failed from the landing figure, and at
 *            the right how long ago the gate shipped it, when that is under a week.
 * Does not:  Read anything (the path hands it the totals and the build's facts) or print the results file's suites.
 * Used by:   ReviewPath.tsx.
 */
import { PATH_GATE_PARTS } from '../../../lib/reviewPath';
import { reviewedWord } from '../../../lib/reviewMirror';
import { TickDisc } from '../TickDisc';
import styles from '../ReviewPath/PathTile.module.css';

export function GateTile({
  label,
  gated,
  totals,
  review,
  shipped,
}: {
  label: string;
  gated: boolean;
  totals: { passed: string; failed: string; version: string };
  review: { reviewed: string } | null;
  /** "shipped 2 h ago", or null when it is not drawn. */
  shipped: string | null;
}) {
  return (
    <div className={styles.shot} data-testid="review-path-gate-tile">
      <p className={styles.cap}>
        {label} {totals.version}
      </p>
      <span className={styles.live}>Live</span>
      <ul className={styles.rows} data-gated={gated}>
        {PATH_GATE_PARTS.map((part, index) => (
          <li key={part} data-ticked={gated}>
            {gated && <TickDisc />}
            {part}
            {index === PATH_GATE_PARTS.length - 1 ? ` ${reviewedWord(review)}` : ''}
          </li>
        ))}
      </ul>
      <p className={styles.foot}>
        <span>
          {totals.passed} passed, {totals.failed} failed
        </span>
        {shipped !== null && (
          <span className={styles.ago} data-testid="review-path-shipped">
            {shipped}
          </span>
        )}
      </p>
    </div>
  );
}
