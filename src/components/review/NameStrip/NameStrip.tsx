/**
 * Does:      Draws the mirror's strip of names (ADR: The review route gives a reason before it asks, the addendum on the
 *            mirror): how many people put their name on it, the link to what they wrote, and the named ones in the
 *            strip's order, each with the title the API gives, then how many more.
 * Does not:  Pick or order the names (reviewMirror.ts's stripOf does), quote anyone, or read the API.
 * Used by:   ReviewPage.tsx.
 */
import type { Recommendation } from '../../../lib/recommendations';
import { MIRROR, stripLabel, stripOf } from '../../../lib/reviewMirror';
import { RECOMMENDATIONS_ADDRESS } from '../../../lib/reviewVerdict';
import styles from './NameStrip.module.css';

export function NameStrip({
  recommendations,
  onFollow,
}: {
  recommendations: Recommendation[] | null;
  onFollow: () => void;
}) {
  const strip = stripOf(recommendations);
  return (
    <section
      className={`${styles.strip} op-glass`}
      aria-labelledby="review-names"
      data-testid="review-names"
    >
      <div className={styles.head}>
        <h2 id="review-names" className={styles.label}>
          {strip.count > 0
            ? stripLabel(strip.count)
            : 'People who worked with him put their name on it'}
        </h2>
        <a
          className={styles.more}
          href={RECOMMENDATIONS_ADDRESS}
          onClick={onFollow}
          data-action="review.recommendations"
        >
          {MIRROR.stripLink}
        </a>
      </div>
      <ul className={styles.names}>
        {strip.shown.map((entry) => (
          <li key={entry.name} className={styles.name}>
            <strong>{entry.name}</strong>, {entry.title}
          </li>
        ))}
        {strip.more > 0 && <li className={styles.name}>and {strip.more} more</li>}
      </ul>
    </section>
  );
}
