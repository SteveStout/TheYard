/**
 * Does:      Draws the review route's voices band as the quality1120 picture draws it (ADR: The review route gives a reason
 *            before it asks, the addendum on the voices band): "Who worked with him says" on the left, "Read all N named
 *            recommendations" on the right, then six named voices, each its own card with its own "Read it in full",
 *            three across and two rows at a desk, one column on a phone. No voice belongs to a question above it. The
 *            About page draws the same band in two runs of three, the first under its own number and head, the second
 *            bare further down (ADR-142, the addendum on the landing views), its presses reported under its own name.
 * Does not:  Pick the names (voices.ts does) or read the API (the page hands the answer over). A name the answer does
 *            not hold draws nothing; before the answer arrives the band is not drawn.
 * Used by:   ReviewPage.tsx, AboutPage.tsx.
 */
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import { recommendationsLink, RECOMMENDATIONS_ADDRESS, VERDICT } from '../../../lib/reviewVerdict';
import type { Recommendation } from '../../../lib/recommendations';
import { VOICES, voiceOf } from '../../../lib/voices';
import { Voice } from '../Voice';
import { SectionHead } from '../SectionHead';
import styles from './ReviewVoices.module.css';

export function ReviewVoices({
  recommendations,
  onFollow,
  number = '03',
  action = 'review.recommendations',
  from = 0,
  to,
  bare = false,
}: {
  /** The /api/recommendations answer the page holds, or null before it arrives. */
  recommendations: Recommendation[] | null;
  /** Reports a press on a voice or on the link to all of them. */
  onFollow: () => void;
  /** The section's badge on a page that numbers its sections; none, no badge. */
  number?: string | null;
  /** What a press on the link to all of them reports. */
  action?: string;
  /** The first voice of this run, by its place in the band. */
  from?: number;
  /** The voice after this run's last; none, to the band's end. */
  to?: number;
  /** A later run of the band: the voices alone, under no head of their own. */
  bare?: boolean;
}) {
  // The holder of its number badge's one pass of light.
  const [holder] = useFirstSeen<HTMLElement>();
  const names = VOICES.band
    .filter((name) => voiceOf(recommendations, name) !== null)
    .slice(from, to);
  if (recommendations === null || names.length === 0) return null;
  const grid = (
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
  );
  if (bare)
    return (
      <section
        className={styles.bare}
        aria-label={VERDICT.quoteLabel}
        data-mock="voices-more"
        data-testid="review-voices-more"
      >
        {grid}
      </section>
    );
  return (
    <section
      ref={holder}
      id="voices"
      aria-labelledby="review-voices-title"
      data-mock="voices"
      data-testid="review-voices"
    >
      <SectionHead
        id="review-voices-title"
        number={number ?? undefined}
        title={VERDICT.quoteLabel}
        meta={
          <a
            className={styles.all}
            href={RECOMMENDATIONS_ADDRESS}
            onClick={onFollow}
            data-action={action}
            data-testid="review-voices-all"
          >
            {recommendationsLink(recommendations.length)} <span aria-hidden="true">→</span>
          </a>
        }
      />
      {grid}
    </section>
  );
}
