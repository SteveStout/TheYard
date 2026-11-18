/**
 * Does:      Draws the key above the Decision Records index once its marks are read (ADR-029, the addendum on
 *            the marks): what the filled dot and the ring mean, with how many records each holds. A mark nobody
 *            can read is a decoration, and the key is what makes it a reading.
 * Does not:  Read the marks (useRecordMarks.ts does) or draw a row's own mark (RailDocRow.tsx does).
 * Used by:   SideNav.tsx, through the folder's index.ts.
 */
import { heldCounts, type Enforced } from '../../../lib/enforced';
import styles from '../SideNav/SideNav.module.css';

/** One line: the dot and its count, then the ring and its count. */
export function RailHeldKey({ enforced }: { enforced: Enforced }) {
  const { test, review } = heldCounts(enforced);
  return (
    <p className={styles.heldKey} data-testid="records-held-key">
      <span className={styles.heldMark} data-held="test" aria-hidden="true" /> {test} held by a test
      <span className={styles.heldKeyGap} aria-hidden="true" />
      <span className={styles.heldMark} data-held="review" aria-hidden="true" /> {review} by review
    </p>
  );
}
