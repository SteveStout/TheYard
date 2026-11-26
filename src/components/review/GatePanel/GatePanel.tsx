/**
 * Does:      Draws the mirror's live panel (ADR: The review route gives a reason before it asks, the addendum on the
 *            mirror): the gate that shipped this version, its six fixed parts ticked only when the build carries the
 *            gate's own reading of this version, CodeRabbit's word, the tests passed and failed, the quality numbers
 *            held when there is a reading, and the honest line, "It slipped once.", with its link to the turn.
 * Does not:  Read anything (the mirror hands it the summary and the build's facts) or print the results file's suites.
 * Used by:   ReviewMirror.tsx.
 */
import type { MouseEvent } from 'react';
import { ICON } from '../../../lib/icons';
import type { TestSummary } from '../../../lib/landingProof';
import {
  GATE_PARTS,
  gateTotals,
  MIRROR,
  reviewedWord,
  reviewWord,
} from '../../../lib/reviewMirror';
import { SLIP_ADDRESS, type HeldQuality } from '../../../lib/reviewVerdict';
import styles from './GatePanel.module.css';

export function GatePanel({
  summary,
  gated,
  version,
  quality,
  review,
  onSlip,
}: {
  summary: TestSummary | null;
  /** The build carries the gate's own reading of this version. */
  gated: boolean;
  /** The build's version, until the summary names its own. */
  version: string;
  quality: HeldQuality;
  review: { reviewed: string } | null;
  onSlip: (event: MouseEvent<HTMLAnchorElement>) => void;
}) {
  const totals = gateTotals(summary, version);
  return (
    <aside
      className={`${styles.panel} op-glass`}
      aria-labelledby="review-gate-title"
      data-testid="review-gate-panel"
    >
      <p className={styles.label}>
        <span>{MIRROR.panelLabel}</span>
        <span className={styles.live}>
          <span className={styles.dot} aria-hidden="true" />
          Live
        </span>
      </p>
      <h2 id="review-gate-title" className={styles.title}>
        {MIRROR.panelTitle}
      </h2>
      <p className={styles.line}>
        Version {totals.version}. {MIRROR.panelLine}
      </p>
      <ul className={styles.parts} data-gated={gated}>
        {GATE_PARTS.map((part, index) => {
          // CodeRabbit's part is ticked only when it read this version in full; otherwise a dash, never green.
          const last = index === GATE_PARTS.length - 1;
          const ticked = gated && (!last || reviewWord(review) === 'full');
          return (
            <li key={part} className={styles.part}>
              <span className={styles.check} data-ticked={ticked} aria-hidden="true">
                {gated && (
                  <svg viewBox="0 0 20 20" width={ICON.sm} height={ICON.sm} focusable="false">
                    <path
                      d={ticked ? 'M5 10.5l3.2 3.2L15 7' : 'M6 10h8'}
                      fill="none"
                      stroke="currentColor"
                      strokeWidth={ICON.stroke}
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    />
                  </svg>
                )}
              </span>
              <span className={styles.partName}>{part}</span>
              {last && <em className={styles.word}>{reviewedWord(review)}</em>}
            </li>
          );
        })}
      </ul>
      <dl className={styles.sums}>
        <div className={styles.sum}>
          <dt className={styles.figure} data-testid="review-gate-passed">
            {totals.passed}
          </dt>
          <dd>tests passed, {totals.failed} failed</dd>
        </div>
        {quality !== null && (
          <div className={styles.sum}>
            <dt className={styles.figure}>
              {quality.held} of {quality.of}
            </dt>
            <dd>quality numbers held</dd>
          </div>
        )}
      </dl>
      <p className={styles.slip}>
        <strong>{MIRROR.slipLead}</strong> {MIRROR.slip}{' '}
        <a href={SLIP_ADDRESS} onClick={onSlip} data-action="review.slip" data-testid="review-slip">
          {MIRROR.slipLink}
        </a>
      </p>
    </aside>
  );
}
