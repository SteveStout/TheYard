/**
 * Does:      Draws the review route's first screen (ADR: The review route gives a reason before it asks): the eyebrow,
 *            the claim, the lede, three proof tiles (two from the record, one live from this build), the one primary
 *            move with Book 20 minutes and the resume beside it, the line under them, and at the side one named quote
 *            and the roles card. Every button reports its action; the primary opens the top of Quality in place, and a
 *            modified click follows its address.
 * Does not:  Read the API (useReviewReadings.ts does) or hold the words (reviewVerdict.ts does).
 * Used by:   ReviewPage.tsx.
 */
import type { MouseEvent } from 'react';
import type { DocKey } from '../../../library/documents';
import { landOnSection } from '../../../library/pendingAnchor';
import type { TestSummary } from '../../../lib/landingProof';
import type { Recommendation } from '../../../lib/recommendations';
import type { ReviewAction } from '../../../lib/reviewProgress';
import {
  BOOK_ADDRESS,
  leadQuote,
  PROOF,
  PROOF_ADDRESS,
  RECORD_TILES,
  RESUME_ADDRESS,
  ROLE_LINKS,
  testsTile,
  VERDICT,
  type HeldQuality,
} from '../../../lib/reviewVerdict';
import { ProofTile } from '../ProofTile';
import { ReviewLeadQuote } from '../ReviewLeadQuote';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewVerdict.module.css';

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

export function ReviewVerdict({
  summary,
  recommendations,
  quality,
  onAction,
  onOpenDoc,
}: {
  summary: TestSummary | null;
  recommendations: Recommendation[] | null;
  /** The quality numbers this build held, or null when it has no reading of its own. */
  quality: HeldQuality;
  onAction: (action: ReviewAction) => void;
  onOpenDoc: (key: DocKey) => void;
}) {
  const tiles = [...RECORD_TILES, testsTile(summary, quality)];
  const quote = leadQuote(recommendations);
  const openProof = (event: MouseEvent<HTMLAnchorElement>) => {
    onAction('proof');
    if (!plainClick(event)) return;
    // Opened in place at the top of Quality, so the page is not loaded again.
    event.preventDefault();
    landOnSection('');
    onOpenDoc(PROOF.doc);
  };

  return (
    <div className={styles.verdict} data-testid="review-verdict">
      <div className={styles.main}>
        <p className={styles.eyebrow}>
          {VERDICT.eyebrow.map((part, index) => (
            <span key={part}>
              {index > 0 && (
                <span className={styles.slash} aria-hidden="true">
                  /
                </span>
              )}
              {part}
            </span>
          ))}
        </p>
        <h1 id="review-title" className={styles.title}>
          {VERDICT.title[0]} <span className={styles.line}>{VERDICT.title[1]}</span>
        </h1>
        <p className={styles.lede}>{VERDICT.lede}</p>
        <ul className={styles.tiles} aria-label="The proof">
          {tiles.map((tile) => (
            <ProofTile key={tile.key} tile={tile} />
          ))}
        </ul>
        <div className={styles.moves}>
          <a
            className={`${pills.primary} ${styles.proof}`}
            href={PROOF_ADDRESS}
            onClick={openProof}
            data-action="review.proof"
            data-testid="review-proof"
          >
            {VERDICT.primary}
            <span aria-hidden="true">→</span>
          </a>
          <a
            className={`${pills.secondary} ${styles.book}`}
            href={BOOK_ADDRESS}
            onClick={() => onAction('book')}
            data-action="review.book"
            data-testid="review-book"
          >
            {VERDICT.book}
          </a>
          <a
            className={styles.resume}
            href={RESUME_ADDRESS}
            onClick={() => onAction('resume')}
            data-action="review.resume"
          >
            {VERDICT.resume}
          </a>
        </div>
        <p className={styles.under}>{VERDICT.under}</p>
      </div>
      <aside className={styles.side} aria-label="Who vouches for him">
        <ReviewLeadQuote
          quote={quote}
          count={recommendations?.length ?? 0}
          onFollow={() => onAction('recommendations')}
        />
        <section className={`${styles.roles} op-glass`} aria-label="Roles">
          <p className={styles.rolesLine}>{VERDICT.roles}</p>
          <ul className={styles.roleLinks}>
            {ROLE_LINKS.map((link) => (
              <li key={link.action}>
                <a
                  className={pills.secondary}
                  href={link.href}
                  onClick={() => onAction(link.action)}
                  data-action={`review.${link.action}`}
                >
                  {link.label}
                </a>
              </li>
            ))}
          </ul>
        </section>
      </aside>
    </div>
  );
}
