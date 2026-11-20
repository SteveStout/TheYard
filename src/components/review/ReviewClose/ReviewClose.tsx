/**
 * Does:      Draws the review route's close as the sales1120 picture draws it (ADR: The sales pages set the site's look),
 *            one glass row at the page's width, "06 Talk": the site's one closing row (AskClose), the closing question
 *            with its number badge and its line on the left and its moves on the right (under it on a phone): Book 20
 *            minutes (the filled one; the booking is the email, and it opens the booking sheet too), the gold Resume
 *            (PDF) and Message me on LinkedIn, each reporting its action, and under them the send line, which hands the
 *            page's own address to the phone's share sheet or, where there is none, copies it and says "Link copied"
 *            for two seconds. The home page draws the same close, the same question under its own 08 and no send
 *            line, with "Next step" as the heading a screen reader lands on (ADR-082, the addendum on the home page's
 *            order).
 * Does not:  Draw a voice (ReviewVoices.tsx does) or read the API.
 * Used by:   ReviewPage.tsx, Landing.tsx.
 */
import { useEffect, useState } from 'react';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import { FORWARD, forwardText } from '../../../lib/reviewBook';
import type { ReviewAction } from '../../../lib/reviewProgress';
import { AskClose } from '../../shared/AskClose';
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
  // The home page's form: its question is drawn as words under a heading a screen reader lands on ("Next step").
  const home = heading !== undefined;
  return (
    <AskClose
      holder={holder}
      id={home ? undefined : 'talk'}
      headingId="review-close-title"
      className={styles.close}
      testId="review-close"
      action="review"
      spaced={false}
      form={home ? 'home' : undefined}
      heading={heading}
      number={ask.number}
      question={ask.question}
      line={ask.line}
      onAction={onAction}
      onBook={onBook}
      after={
        address && (
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
        )
      }
    />
  );
}
