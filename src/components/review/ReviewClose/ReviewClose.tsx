/**
 * Does:      Draws the review route's close as the whole-page picture draws it, one glass row at the page's width: "That
 *            is the work", Anthony Fortner's named voice, and three buttons, Book 20 minutes (the filled one; the
 *            booking is the email), Resume (PDF) and Message me on LinkedIn, each reporting its action.
 * Does not:  Pick the name (voices.ts does) or read the API (useReviewReadings.ts does).
 * Used by:   ReviewPage.tsx.
 */
import type { Recommendation } from '../../../lib/recommendations';
import type { ReviewAction } from '../../../lib/reviewProgress';
import {
  BOOK_ADDRESS,
  CLOSE,
  LINKEDIN_ADDRESS,
  RESUME_ADDRESS,
  VERDICT,
} from '../../../lib/reviewVerdict';
import { VOICES } from '../../../lib/voices';
import { Voice } from '../../shared/Voice';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewClose.module.css';

export function ReviewClose({
  recommendations,
  onAction,
}: {
  recommendations: Recommendation[] | null;
  onAction: (action: ReviewAction) => void;
}) {
  return (
    <section
      className={`${styles.close} op-glass`}
      aria-labelledby="review-close-title"
      data-testid="review-close"
    >
      <div>
        <h2 id="review-close-title" className={styles.readout}>
          {CLOSE.readout}
        </h2>
        {VOICES.close.map((name) => (
          <Voice
            key={name}
            name={name}
            recommendations={recommendations}
            onFollow={() => onAction('recommendations')}
            plain
          />
        ))}
      </div>
      <div className={styles.acts}>
        <a
          className={`${pills.primary} ${styles.act}`}
          href={BOOK_ADDRESS}
          onClick={() => onAction('book')}
          data-action="review.book"
        >
          {VERDICT.book}
        </a>
        <a
          className={`${pills.secondary} ${styles.act}`}
          href={RESUME_ADDRESS}
          onClick={() => onAction('resume')}
          data-action="review.resume"
        >
          {VERDICT.resume}
        </a>
        <a
          className={`${pills.secondary} ${styles.act}`}
          href={LINKEDIN_ADDRESS}
          rel="noopener"
          onClick={() => onAction('linkedin')}
          data-action="review.linkedin"
        >
          {CLOSE.linkedin}
        </a>
      </div>
    </section>
  );
}
