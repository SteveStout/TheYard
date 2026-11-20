/**
 * Does:      Draws the review route's close as the whole-page picture draws it, one glass row at the page's width: "That
 *            is the work", Anthony Fortner's named voice, and three buttons, Book 20 minutes (the filled one; the
 *            booking is the email, and it opens the booking sheet too), Resume (PDF) and Message me on LinkedIn, each
 *            reporting its action, and under them the send line, which hands the page's own address to the phone's
 *            share sheet or, where there is none, copies it and says "Link copied" for two seconds.
 * Does not:  Pick the name (voices.ts does) or read the API (useReviewReadings.ts does).
 * Used by:   ReviewPage.tsx.
 */
import { useEffect, useState } from 'react';
import type { Recommendation } from '../../../lib/recommendations';
import { FORWARD, forwardText } from '../../../lib/reviewBook';
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
  address,
  onAction,
  onBook,
}: {
  recommendations: Recommendation[] | null;
  /** The address the send line shares: this route with its for value and the visitor's code. */
  address: () => string;
  onAction: (action: ReviewAction) => void;
  onBook: () => void;
}) {
  const [said, setSaid] = useState<string | null>(null);
  useEffect(() => {
    if (said === null) return;
    const id = window.setTimeout(() => setSaid(null), 2000);
    return () => window.clearTimeout(id);
  }, [said]);
  const forward = () => {
    onAction('forward');
    const url = address();
    if (typeof navigator.share === 'function') {
      void navigator.share({ title: FORWARD.title, text: forwardText(url) }).catch(() => {});
      return;
    }
    // navigator.clipboard is undefined outside a secure context, as the document window's Copy link knows.
    const clipboard = navigator.clipboard;
    if (!clipboard) {
      setSaid(FORWARD.refused);
      return;
    }
    void clipboard
      .writeText(url)
      .then(() => setSaid(FORWARD.copied))
      .catch(() => setSaid(FORWARD.refused));
  };
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
          onClick={() => {
            onAction('book');
            onBook();
          }}
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
        <button
          type="button"
          className={styles.forward}
          onClick={forward}
          aria-live="polite"
          data-action="review.forward"
          data-testid="review-forward"
        >
          {said ?? FORWARD.line}
        </button>
      </div>
    </section>
  );
}
