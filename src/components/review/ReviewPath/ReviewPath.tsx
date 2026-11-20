/**
 * Does:      Draws the review route's short path under either first screen (ADR: The review route gives a reason before
 *            it asks, the addendum on the short path and the one on the reader's questions) as the whole-page picture
 *            draws it: "Three questions, two minutes" with "N of 3 opened." kept on this browser, then three cards, each
 *            the reader's question, one answer, one live tile of one height and one door named for where it goes,
 *            reporting its action, and under each card the named voice that backs its claim. Quality opens in place
 *            at its top; the gate opens the tests card, with the run on GitHub beside it when the build names one; the
 *            room opens the lot.
 * Does not:  Read anything (useReviewPath.ts and the page do) or count toward the seven-stop tour's place.
 * Used by:   ReviewPage.tsx.
 */
import type { MouseEvent } from 'react';
import type { DocKey } from '../../../library/documents';
import { landOnSection } from '../../../library/pendingAnchor';
import type { Recommendation } from '../../../lib/recommendations';
import type { ReviewAction } from '../../../lib/reviewProgress';
import {
  PATH,
  PATH_STOPS,
  pathProgress,
  stopAddress,
  storeTotal,
  type PathLot,
  type PathStop,
} from '../../../lib/reviewPath';
import { gateRunUrl } from '../../layout/StoreBar/gateFacts';
import { PROOF, type HeldQuality } from '../../../lib/reviewVerdict';
import { VOICES } from '../../../lib/voices';
import { Voice } from '../../shared/Voice';
import { GateTile } from '../GateTile';
import { LotTile } from '../LotTile';
import { QualityTrendTile } from '../QualityTrendTile';
import { ReviewHead } from '../ReviewHead';
import styles from './ReviewPath.module.css';

/** The room's sentence before the store's count arrives: the sentence on its own, its first letter a capital. */
const roomPayoff = (payoff: string) => payoff.charAt(0).toUpperCase() + payoff.slice(1);

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

export function ReviewPath({
  reached,
  readings,
  lot,
  gate,
  totals,
  shipped,
  recommendations,
  now,
  onReach,
  onAction,
  onOpenDoc,
}: {
  reached: string[];
  readings: string[] | null;
  lot: PathLot | null;
  gate: {
    gated: boolean;
    quality: HeldQuality;
    review: { reviewed: string } | null;
    /** The public gate run that shipped this version, or null when the build names none. */
    run: number | null;
  };
  totals: { passed: string; failed: string; version: string };
  /** "shipped 2 h ago" under the gate's tile, or null when the run's time is absent or over a week old. */
  shipped: string | null;
  /** The /api/recommendations answer the page holds, for the voice under each card. */
  recommendations: Recommendation[] | null;
  /** The page's clock, for the lot's countdown. */
  now: number;
  onReach: (key: string) => void;
  onAction: (action: ReviewAction) => void;
  onOpenDoc: (key: DocKey) => void;
}) {
  const open = (stop: PathStop) => (event: MouseEvent<HTMLAnchorElement>) => {
    onAction(stop.action);
    onReach(stop.key);
    if (stop.key !== 'quality' || !plainClick(event)) return;
    // Quality opens in place, at its top.
    event.preventDefault();
    landOnSection('');
    onOpenDoc(PROOF.doc);
  };
  const tile = (stop: PathStop) =>
    stop.key === 'quality' ? (
      <QualityTrendTile label={stop.tile} readings={readings} quality={gate.quality} />
    ) : stop.key === 'gate' ? (
      <GateTile
        label={stop.tile}
        gated={gate.gated}
        totals={totals}
        review={gate.review}
        shipped={shipped}
      />
    ) : (
      <LotTile label={stop.tile} lot={lot} now={now} />
    );

  return (
    <section aria-labelledby="review-path-title" data-testid="review-path">
      <ReviewHead
        id="review-path-title"
        title={PATH.title}
        meta={<strong data-testid="review-path-count">{pathProgress(reached)}</strong>}
        hint={PATH.hint}
      />
      <div className={styles.cards}>
        {PATH_STOPS.map((stop, index) => [
          <article
            key={stop.key}
            className={`${styles.card} op-glass`}
            data-column={index + 1}
            data-testid={`review-path-${stop.key}-stop`}
            data-reached={reached.includes(stop.key)}
          >
            <h3 className={styles.top}>
              <span className={styles.number} aria-hidden="true">
                {index + 1}
              </span>
              <span className={styles.title}>{stop.title}</span>
              <span className={styles.time}>{stop.time}</span>
            </h3>
            <p className={styles.payoff}>
              {stop.key !== 'room' ? (
                stop.payoff
              ) : lot === null ? (
                roomPayoff(stop.payoff)
              ) : (
                <>
                  <span data-testid="review-path-total">{storeTotal(lot)}</span> {stop.payoff}
                </>
              )}
            </p>
            {tile(stop)}
            <div className={styles.acts}>
              <a
                className={`${index === 0 ? 'op-pill-primary' : 'op-pill-secondary'} ${styles.open}`}
                href={stopAddress(stop.key, lot)}
                onClick={open(stop)}
                data-action={`review.stop.${stop.key}`}
                data-testid={`review-path-${stop.key}`}
              >
                {stop.door}
                <span aria-hidden="true">→</span>
              </a>
              {stop.key === 'gate' && gate.run !== null && (
                <a
                  className={styles.textLink}
                  href={gateRunUrl(gate.run)}
                  rel="noopener"
                  onClick={() => onAction('gate-run')}
                  data-action="review.gate-run"
                  data-testid="review-path-gate-run"
                >
                  {PATH.gateRun}
                </a>
              )}
            </div>
          </article>,
          ...VOICES[stop.key].map((name) => (
            <Voice
              key={name}
              name={name}
              recommendations={recommendations}
              onFollow={() => onAction('recommendations')}
              className={styles.voice}
              column={index + 1}
            />
          )),
        ])}
      </div>
    </section>
  );
}
