/**
 * Does:      Draws the Quality sheet's foot (ADR-130, the addendum on the panel, the numbers and the foot): one last card
 *            after the numbered ones, the review route's closing question and its line beside the same three moves,
 *            Book 20 minutes, the gold Resume (PDF) and Message me on LinkedIn, each reporting the action its twin on
 *            the review route reports. The close is the sheet's last card: no Where next list sits under it (ADR-132's
 *            addendum, three pages end on their close). While the sheet is open it plays the cards' motions
 *            (watchSeen): each badge's pass of light, the panel's live dot.
 * Does not:  Hold the question's words (reviewVerdict.ts does) or open the booking sheet (the email the address sends
 *            is the booking).
 * Used by:   DocDialog.tsx.
 */
import { useEffect, useRef } from 'react';
import { watchSeen } from '../../../hooks/useFirstSeen';
import { reportAction } from '../../../hooks/useReviewProgress';
import { CLOSE, CLOSE_MOVES } from '../../../lib/reviewVerdict';
import { PdfMark } from '../PdfMark';
import styles from './QualityFoot.module.css';

export function QualityFoot() {
  const foot = useRef<HTMLElement>(null);
  // The cards before the foot are markup the document drew; they are watched for their motions from here.
  useEffect(() => {
    const page = foot.current?.parentElement;
    if (!page || !foot.current) return;
    return watchSeen([
      ...page.querySelectorAll('[data-mock="q-card"], [data-mock="live-panel"]'),
      foot.current,
    ]);
  }, []);
  return (
    <section
      ref={foot}
      className={`doc-panel op-glass ${styles.foot}`}
      aria-label={CLOSE.label}
      data-mock="close"
      data-testid="quality-foot"
    >
      <div className="op-ask" data-mock="close-ask">
        <div>
          <p className="op-ask-q" data-mock="close-question">
            {CLOSE.question}
          </p>
          <p className="op-ask-sub">{CLOSE.line}</p>
        </div>
        <div className={styles.acts} data-mock="close-moves">
          {CLOSE_MOVES.map((move) => (
            <a
              key={move.action}
              className={move.look}
              href={move.href}
              rel={move.action === 'linkedin' ? 'noopener' : undefined}
              onClick={() => reportAction(move.action)}
              data-action={`review.${move.action}`}
            >
              {move.action === 'resume' && <PdfMark />}
              {move.label}
            </a>
          ))}
        </div>
      </div>
    </section>
  );
}
