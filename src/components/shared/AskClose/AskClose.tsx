/**
 * Does:      Draws the one closing row every public page a reader is sent to ends on (ADR: The sales pages set the site's
 *            look, the addendum on the one close): a glass card holding the question "What has to work, where is it
 *            today, and what does success look like?", its line, and three moves: Book 20 minutes (the filled pill, the
 *            booking email, which opens the booking sheet too), the gold Resume (PDF) and Message me on LinkedIn (the
 *            outlined pill). The review route draws it with its 06 badge and its send line, the home page with its 08
 *            badge under a heading a screen reader lands on; a blog post and the recommendations page draw it bare.
 *            Every word and address is read from reviewVerdict.ts (CLOSE, CLOSE_MOVES), so they live in one place;
 *            /about, which the API draws, holds the same words (AskCloseHtml.cs, held equal by a test).
 * Does not:  Open the booking sheet (the page does, on onBook), count anything (each address is the counted one the
 *            rest of the site uses; the review route reports its own actions through onAction) or hold a colour of its
 *            own (sales.css draws the row, pills.css the pills).
 * Used by:   ReviewClose.tsx, BlogPage.tsx, RecommendationsPage.tsx.
 */
import type { ReactNode } from 'react';
import { CLOSE, CLOSE_MOVES } from '../../../lib/reviewVerdict';
import { PdfMark } from '../PdfMark';
import styles from './AskClose.module.css';

/** The three moves, the names a page reports them under. */
export type AskMove = (typeof CLOSE_MOVES)[number]['action'];

export function AskClose({
  onBook,
  onAction,
  action = 'next',
  number,
  question = CLOSE.question,
  line = CLOSE.line,
  heading,
  form,
  after,
  holder,
  spaced = true,
  id,
  headingId = 'ask-close-title',
  className = '',
  testId = 'ask-close',
}: {
  /** Opens the booking sheet beside the email app. */
  onBook: () => void;
  /** Reports a move, on a page that counts its own; the others are counted by their addresses. */
  onAction?: (move: AskMove) => void;
  /** The prefix of each move's data-action: the review route's own, or next on the pages it leads to. */
  action?: string;
  /** The number badge before the question, on a page whose sections are numbered. */
  number?: string;
  /** The question, the review route's own unless a page names it. */
  question?: string;
  /** The line under the question, the review route's own unless a page names it. */
  line?: string;
  /** A heading a screen reader lands on, the question then drawn as words under it (the home page's form). */
  heading?: string;
  /** The form a page's own sheet lays the row out by (data-form). */
  form?: string;
  /** What follows the three moves inside their column: the review route's send line. */
  after?: ReactNode;
  /** The ref a page's motions watch the card through. */
  holder?: (element: HTMLElement | null) => void;
  /** A step above the card; off where the page spaces its own sections. */
  spaced?: boolean;
  id?: string;
  headingId?: string;
  className?: string;
  testId?: string;
}) {
  const badge = number && (
    <span className="op-num" data-mock="section-num">
      {number}
    </span>
  );
  return (
    <section
      ref={holder}
      id={id}
      className={[styles.card, spaced ? styles.spaced : '', className, 'op-glass']
        .filter(Boolean)
        .join(' ')}
      aria-labelledby={headingId}
      data-form={form}
      data-mock="close"
      data-testid={testId}
    >
      {heading !== undefined && (
        <h2 id={headingId} className={styles.srOnly}>
          {heading}
        </h2>
      )}
      <div className="op-ask" data-mock="close-ask">
        <div>
          {heading !== undefined ? (
            <p className="op-ask-q" data-mock="close-question">
              {badge}
              {question}
            </p>
          ) : (
            <h2 id={headingId} className="op-ask-q" data-mock="close-question">
              {badge}
              {question}
            </h2>
          )}
          <p className="op-ask-sub">{line}</p>
        </div>
        <div className={styles.moves} data-mock="close-moves">
          {CLOSE_MOVES.map((move) => (
            <a
              key={move.action}
              className={`${move.look} ${move.look === 'op-pill-resume' ? styles.resume : styles.move}`}
              href={move.href}
              rel={move.action === 'linkedin' ? 'noopener' : undefined}
              onClick={() => {
                onAction?.(move.action);
                if (move.action === 'book') onBook();
              }}
              data-action={`${action}.${move.action}`}
              data-testid={move.action === 'resume' ? `${testId}-resume` : undefined}
            >
              {move.action === 'resume' && <PdfMark />}
              {move.label}
            </a>
          ))}
          {after}
        </div>
      </div>
    </section>
  );
}
