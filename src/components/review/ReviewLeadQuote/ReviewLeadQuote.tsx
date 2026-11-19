/**
 * Does:      Draws the one quote on the review route's first screen (ADR: The review route gives a reason before it
 *            asks): who worked with him says, the pull sentence, the writer's name and title as the API gives them,
 *            and the link to all the recommendations with the answer's own count.
 * Does not:  Pick the quote (reviewVerdict.ts's leadQuote does) or read the API (useReviewReadings.ts does).
 * Used by:   ReviewVerdict.tsx.
 */
import type { Recommendation } from '../../../lib/recommendations';
import { RECOMMENDATIONS_ADDRESS, recommendationsLink, VERDICT } from '../../../lib/reviewVerdict';
import styles from './ReviewLeadQuote.module.css';

export function ReviewLeadQuote({
  quote,
  count,
  onFollow,
}: {
  /** The quote to show, or null while the answer is out: the panel keeps its place. */
  quote: Recommendation | null;
  /** How many recommendations the answer holds. */
  count: number;
  /** The link to all of them was followed. */
  onFollow: () => void;
}) {
  return (
    <figure className={`${styles.panel} op-glass`} data-testid="review-lead-quote">
      <p className={styles.label}>{VERDICT.quoteLabel}</p>
      <blockquote className={styles.quote}>
        <p>{quote === null ? '' : `"${quote.pull}"`}</p>
      </blockquote>
      <figcaption className={styles.who}>
        <strong>{quote?.name}</strong>
        <span>{quote?.title}</span>
      </figcaption>
      <a
        className={styles.more}
        href={RECOMMENDATIONS_ADDRESS}
        onClick={onFollow}
        data-action="review.recommendations"
      >
        {count > 0 ? recommendationsLink(count) : 'Read the named recommendations'}
      </a>
    </figure>
  );
}
