/**
 * Does:      Draws the review route's first screen (ADR: The review route gives a reason before it asks, and ADR: The
 *            sales pages set the site's look) as the sales1120 picture draws it: the eyebrow, the claim, the lede, three
 *            proof tiles (two from the record, one live from this build), each a door to its case card, the first moves
 *            in order (See the work, the gold Resume (PDF), Book 20 minutes), the roles row under them, and at the side
 *            the dark live panel, "TheYard, right now", over the one named quote. Every button reports its action; See
 *            the work and the tiles' doors scroll down to the work and report work.
 * Does not:  Read the API (useReviewReadings.ts and usePulse.ts do) or hold the words (reviewVerdict.ts does).
 * Used by:   ReviewPage.tsx.
 */
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import type { TestSummary } from '../../../lib/landingProof';
import type { NowRow } from '../../../lib/nowPanel';
import type { Recommendation } from '../../../lib/recommendations';
import { WORK_ID } from '../../../lib/reviewCases';
import type { ReviewAction } from '../../../lib/reviewProgress';
import {
  BOOK_ADDRESS,
  leadQuote,
  RECORD_TILES,
  RESUME_ADDRESS,
  ROLE_LINKS,
  testsTile,
  VERDICT,
  type HeldQuality,
} from '../../../lib/reviewVerdict';
import { scrollToSection } from '../../../lib/sectionScroll';
import { NowPanel } from '../../shared/NowPanel';
import { PdfMark } from '../../shared/PdfMark';
import { ProofTile } from '../../shared/ProofTile';
import { ReviewLeadQuote } from '../ReviewLeadQuote';
import styles from './ReviewVerdict.module.css';

export function ReviewVerdict({
  summary,
  recommendations,
  quality,
  rightNow,
  asOf,
  onAction,
  onBook,
}: {
  summary: TestSummary | null;
  recommendations: Recommendation[] | null;
  /** The quality numbers this build held, or null when it has no reading of its own. */
  quality: HeldQuality;
  /** "TheYard, right now": the live panel's rows, each only when read; none, no panel. */
  rightNow: readonly NowRow[];
  /** "as of HH:MM" when the newest read of the pulse failed. */
  asOf: string | null;
  onAction: (action: ReviewAction) => void;
  /** Opens the booking sheet beside the email app. */
  onBook: () => void;
}) {
  const tiles = [...RECORD_TILES, testsTile(summary, quality)];
  const quote = leadQuote(recommendations);
  // The holder of the first move's arrow and the live panel's dot.
  const [holder] = useFirstSeen<HTMLDivElement>();

  return (
    <div ref={holder} className={styles.verdict} data-testid="review-verdict">
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
            <ProofTile key={tile.key} tile={tile} onDoor={() => onAction('work')} />
          ))}
        </ul>
        <div className={styles.moves} data-mock="hero-moves">
          <a
            className={`op-pill-primary ${styles.proof}`}
            href={`#${WORK_ID}`}
            onClick={(event) => {
              onAction('work');
              scrollToSection(event, WORK_ID);
            }}
            data-action="review.work"
            data-testid="review-proof"
          >
            {VERDICT.primary}
            <span className="op-nudge" aria-hidden="true">
              ↓
            </span>
          </a>
          <a
            className="op-pill-resume"
            href={RESUME_ADDRESS}
            onClick={() => onAction('resume')}
            data-action="review.resume"
            data-testid="review-resume"
          >
            <PdfMark />
            {VERDICT.resume}
          </a>
          <a
            className={`op-pill-secondary ${styles.book}`}
            href={BOOK_ADDRESS}
            onClick={() => {
              onAction('book');
              onBook();
            }}
            data-action="review.book"
            data-testid="review-book"
          >
            {VERDICT.book}
          </a>
        </div>
        <section
          className={`${styles.roles} op-glass op-roles-row`}
          aria-label="Roles"
          data-mock="roles"
        >
          <p className={styles.rolesLine}>{VERDICT.roles}</p>
          <ul className={styles.roleLinks}>
            {ROLE_LINKS.map((link) => (
              <li key={link.action}>
                <a
                  className="op-pill-secondary"
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
      </div>
      <aside className={styles.side} aria-label="Who vouches for him" data-mock="hero-side">
        <NowPanel
          id="live-now"
          title="TheYard, right now"
          live
          rows={rightNow}
          asOf={asOf}
          hook="live-panel"
        />
        <ReviewLeadQuote
          quote={quote}
          count={recommendations?.length ?? 0}
          onFollow={() => onAction('recommendations')}
        />
      </aside>
    </div>
  );
}
