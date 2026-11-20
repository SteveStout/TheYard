/**
 * Does:      Draws the review route's voices band as the quality1120 picture draws it (ADR: The review route gives a reason
 *            before it asks, the addendum on the voices band): "Who worked with him says" on the left, "Read all N named
 *            recommendations" on the right, then six named voices, each its own card with its own "Read it in full",
 *            three across and two rows at a desk, one column on a phone. No voice belongs to a question above it.
 * Does not:  Pick the names (voices.ts does) or read the API (the page hands the answer over). A name the answer does
 *            not hold draws nothing; before the answer arrives the band is not drawn.
 * Used by:   ReviewPage.tsx.
 */
import { recommendationsLink, RECOMMENDATIONS_ADDRESS, VERDICT } from '../../../lib/reviewVerdict';
import type { Recommendation } from '../../../lib/recommendations';
import { VOICES, voiceOf } from '../../../lib/voices';
import { Voice } from '../../shared/Voice';
import { SectionHead } from '../../shared/SectionHead';
import styles from './ReviewVoices.module.css';

export function ReviewVoices({
  recommendations,
  onFollow,
}: {
  /** The /api/recommendations answer the page holds, or null before it arrives. */
  recommendations: Recommendation[] | null;
  /** Reports a press on a voice or on the link to all of them. */
  onFollow: () => void;
}) {
  const names = VOICES.band.filter((name) => voiceOf(recommendations, name) !== null);
  if (recommendations === null || names.length === 0) return null;
  return (
    <section
      id="voices"
      aria-labelledby="review-voices-title"
      data-mock="voices"
      data-testid="review-voices"
    >
      <SectionHead
        id="review-voices-title"
        number="03"
        title={VERDICT.quoteLabel}
        meta={
          <a
            className={styles.all}
            href={RECOMMENDATIONS_ADDRESS}
            onClick={onFollow}
            data-action="review.recommendations"
            data-testid="review-voices-all"
          >
            {recommendationsLink(recommendations.length)} <span aria-hidden="true">→</span>
          </a>
        }
      />
      <div className={styles.grid}>
        {names.map((name) => (
          <Voice
            key={name}
            name={name}
            recommendations={recommendations}
            onFollow={onFollow}
            className={styles.card}
            hook="voice"
          />
        ))}
      </div>
    </section>
  );
}
