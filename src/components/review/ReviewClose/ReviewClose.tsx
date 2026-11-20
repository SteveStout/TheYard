/**
 * Does:      Draws the review route's close as the sales1120 picture draws it (ADR: The sales pages set the site's look),
 *            one glass row at the page's width, "06 Talk": the closing question with its number badge and its line on
 *            the left and its moves on the right (under it on a phone): Book 20 minutes (the filled one; the booking is
 *            the email, and it opens the booking sheet too), the gold Resume (PDF) and Message me on LinkedIn, each
 *            reporting its action, and under them the send line, which hands the page's own address to the phone's
 *            share sheet or, where there is none, copies it and says "Link copied" for two seconds.
 *            The home page draws the same close, the same question under its own 08 and no send line, with "Next step"
 *            as the heading a screen reader lands on (ADR-082, the addendum on the home page's order).
 * Does not:  Draw a voice (ReviewVoices.tsx does) or read the API.
 * Used by:   ReviewPage.tsx, Landing.tsx.
 */
import { useEffect, useState } from 'react';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import { FORWARD, forwardText } from '../../../lib/reviewBook';
import type { ReviewAction } from '../../../lib/reviewProgress';
import {
  BOOK_ADDRESS,
  CLOSE,
  LINKEDIN_ADDRESS,
  RESUME_ADDRESS,
  VERDICT,
} from '../../../lib/reviewVerdict';
import { PdfMark } from '../../shared/PdfMark';
import styles from './ReviewClose.module.css';

export function ReviewClose({
  address,
  onAction,
  onBook,
  ask,
  heading,
}: {
  /** The address the send line shares: this route with its for value and the visitor's code; none, no send line. */
  address?: () => string;
  onAction: (action: ReviewAction) => void;
  onBook: () => void;
  /** The closing question and its line, drawn beside the moves with its number badge (op-ask). */
  ask: { number: string; question: string; line: string };
  /** The home page's form: the heading a screen reader lands on, the question then drawn as words under it. */
  heading?: string;
}) {
  const [said, setSaid] = useState<string | null>(null);
  // The holder of the number badge's one pass of light.
  const [holder] = useFirstSeen<HTMLElement>();
  useEffect(() => {
    if (said === null) return;
    const id = window.setTimeout(() => setSaid(null), 2000);
    return () => window.clearTimeout(id);
  }, [said]);
  const forward = () => {
    onAction('forward');
    const url = address?.() ?? window.location.href;
    const copy = () => {
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
    if (typeof navigator.share !== 'function') return copy();
    // The visitor closing the share sheet is their answer; any other failure copies the address instead.
    navigator.share({ title: FORWARD.title, text: forwardText(url) }).catch((error: unknown) => {
      if (!(error instanceof DOMException && error.name === 'AbortError')) copy();
    });
  };
  const acts = (
    <div className={styles.acts}>
      <a
        className={`op-pill-primary ${styles.act}`}
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
        className={`op-pill-resume ${styles.resume}`}
        href={RESUME_ADDRESS}
        onClick={() => onAction('resume')}
        data-action="review.resume"
        data-testid="review-close-resume"
      >
        <PdfMark />
        {VERDICT.resume}
      </a>
      <a
        className={`op-pill-secondary ${styles.act}`}
        href={LINKEDIN_ADDRESS}
        rel="noopener"
        onClick={() => onAction('linkedin')}
        data-action="review.linkedin"
      >
        {CLOSE.linkedin}
      </a>
      {address && (
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
      )}
    </div>
  );
  // The home page's form: its question is drawn as words under a heading a screen reader lands on ("Next step").
  const home = heading !== undefined;
  return (
    <section
      ref={holder}
      id={home ? undefined : 'talk'}
      className={`${styles.close} op-glass`}
      aria-labelledby="review-close-title"
      data-form={home ? 'home' : undefined}
      data-mock="close"
      data-testid="review-close"
    >
      {home && (
        <h2 id="review-close-title" className={styles.srOnly}>
          {heading}
        </h2>
      )}
      <div className="op-ask" data-mock="close-ask">
        <div>
          {home ? (
            <p className="op-ask-q" data-mock="close-question">
              <span className="op-num" data-mock="section-num">
                {ask.number}
              </span>
              {ask.question}
            </p>
          ) : (
            <h2 id="review-close-title" className="op-ask-q" data-mock="close-question">
              <span className="op-num" data-mock="section-num">
                {ask.number}
              </span>
              {ask.question}
            </h2>
          )}
          <p className="op-ask-sub">{ask.line}</p>
        </div>
        {acts}
      </div>
    </section>
  );
}
