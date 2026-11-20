/**
 * Does:      Draws the review route's second first screen, at /?view=review&for=ai (ADR: The review route gives a reason
 *            before it asks, the addendum on the mirror): written to the hiring manager's own problem with AI-written
 *            code, the answer in his own words, the one primary move to what this version had to pass with Book 20
 *            minutes and the gold resume button beside it, the byline, two record tiles and the live gate panel. Every button reports
 *            its action; the strip of names under it is the page's own section (ReviewPage.tsx draws it).
 * Does not:  Read the API (useReviewReadings.ts does) or hold the words (reviewMirror.ts does).
 * Used by:   ReviewPage.tsx.
 */
import type { MouseEvent } from 'react';
import type { DocKey } from '../../../library/documents';
import { landOnSection } from '../../../library/pendingAnchor';
import type { TestSummary } from '../../../lib/landingProof';
import { GATE_ADDRESS, MIRROR, MIRROR_TILES } from '../../../lib/reviewMirror';
import type { ReviewAction } from '../../../lib/reviewProgress';
import {
  BOOK_ADDRESS,
  PROOF,
  RESUME_ADDRESS,
  VERDICT,
  type HeldQuality,
} from '../../../lib/reviewVerdict';
import { PdfMark } from '../../shared/PdfMark';
import { GatePanel } from '../GatePanel';
import verdict from '../ReviewVerdict/ReviewVerdict.module.css';
import styles from './ReviewMirror.module.css';

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

export function ReviewMirror({
  summary,
  gate,
  onAction,
  onBook,
  onOpenDoc,
}: {
  summary: TestSummary | null;
  /** What the build carries about this version: gated, its version, the quality held and CodeRabbit's read. */
  gate: {
    gated: boolean;
    version: string;
    quality: HeldQuality;
    review: { reviewed: string } | null;
  };
  onAction: (action: ReviewAction) => void;
  /** Opens the booking sheet beside the email app. */
  onBook: () => void;
  onOpenDoc: (key: DocKey) => void;
}) {
  // The slip line opens Quality in place on "The slip, and the turn", as the verdict's primary button does.
  const openSlip = (event: MouseEvent<HTMLAnchorElement>) => {
    onAction('slip');
    if (!plainClick(event)) return;
    event.preventDefault();
    landOnSection(PROOF.anchor);
    onOpenDoc(PROOF.doc);
  };

  return (
    <div className={styles.mirror} data-testid="review-mirror">
      <div className={styles.main}>
        <p className={verdict.eyebrow}>{MIRROR.eyebrow}</p>
        <h1 id="review-title" className={verdict.title}>
          {MIRROR.title}
        </h1>
        <dl className={styles.case}>
          <dt>{MIRROR.problemLabel}</dt>
          <dd>{MIRROR.problem}</dd>
          <dt>{MIRROR.answerLabel}</dt>
          <dd className={styles.answer}>{MIRROR.answer}</dd>
        </dl>
        <div className={verdict.moves}>
          <a
            className={`op-pill-primary ${verdict.proof}`}
            href={GATE_ADDRESS}
            onClick={() => onAction('proof')}
            data-action="review.proof"
            data-testid="review-proof"
          >
            {MIRROR.primary}
            <span aria-hidden="true">→</span>
          </a>
          <a
            className={`op-pill-secondary ${verdict.book}`}
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
        </div>
        <p className={styles.byline}>
          <strong>{MIRROR.byName}</strong>
          {MIRROR.byline}
        </p>
        <ul className={styles.tiles} aria-label="From the record">
          {MIRROR_TILES.map((tile) => (
            <li key={tile.key} className={`${styles.tile} op-glass op-tile`}>
              <strong>{tile.title}</strong>
              <span>{tile.source}</span>
            </li>
          ))}
        </ul>
      </div>
      <GatePanel
        summary={summary}
        gated={gate.gated}
        version={gate.version}
        quality={gate.quality}
        review={gate.review}
        onSlip={openSlip}
      />
    </div>
  );
}
