/**
 * Does:      Draws the review route's short path under either first screen (ADR: The review route gives a reason before
 *            it asks, the addendum on the short path): "N of 3 stops" kept on this browser, then three stops, each one
 *            payoff sentence, one live tile and one button that lands on the payoff and reports its action. Quality
 *            opens in place at "The slip, and the turn"; the gate opens the tests card; the room opens the lot.
 * Does not:  Read anything (useReviewPath.ts and the page do) or count toward the seven-stop tour's place.
 * Used by:   ReviewPage.tsx.
 */
import type { MouseEvent } from 'react';
import type { DocKey } from '../../../library/documents';
import { landOnSection } from '../../../library/pendingAnchor';
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
import { PROOF, type HeldQuality } from '../../../lib/reviewVerdict';
import { GateTile } from '../GateTile';
import { LotTile } from '../LotTile';
import { QualityTrendTile } from '../QualityTrendTile';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewPath.module.css';

/** The room's sentence with the store's own count in front, or, before the count arrives, the sentence on its own. */
const roomPayoff = (payoff: string, total: string) =>
  total === '' ? payoff.charAt(0).toUpperCase() + payoff.slice(1) : `${total} ${payoff}`;

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

export function ReviewPath({
  reached,
  readings,
  lot,
  gate,
  totals,
  onReach,
  onAction,
  onOpenDoc,
}: {
  reached: string[];
  readings: string[] | null;
  lot: PathLot | null;
  gate: { gated: boolean; quality: HeldQuality; review: { reviewed: string } | null };
  totals: { passed: string; failed: string; version: string };
  onReach: (key: string) => void;
  onAction: (action: ReviewAction) => void;
  onOpenDoc: (key: DocKey) => void;
}) {
  const open = (stop: PathStop) => (event: MouseEvent<HTMLAnchorElement>) => {
    onAction(stop.action);
    onReach(stop.key);
    if (stop.key !== 'quality' || !plainClick(event)) return;
    // Quality opens in place, landing on the slip and the turn once the document is drawn.
    event.preventDefault();
    landOnSection(PROOF.anchor);
    onOpenDoc(PROOF.doc);
  };
  const tile = (stop: PathStop) =>
    stop.key === 'quality' ? (
      <QualityTrendTile label={stop.tile} readings={readings} quality={gate.quality} />
    ) : stop.key === 'gate' ? (
      <GateTile label={stop.tile} gated={gate.gated} totals={totals} review={gate.review} />
    ) : (
      <LotTile label={stop.tile} lot={lot} />
    );

  return (
    <section
      className={styles.path}
      aria-labelledby="review-path-progress"
      data-testid="review-path"
    >
      <div className={`${styles.progress} op-glass`}>
        <h2 id="review-path-progress" className={styles.count} data-testid="review-path-count">
          {pathProgress(reached)}
        </h2>
        <p className={styles.place}>{PATH.place}</p>
      </div>
      <ol className={styles.stops}>
        {PATH_STOPS.map((stop, index) => (
          <li
            key={stop.key}
            className={`${styles.stop} op-glass`}
            data-testid={`review-path-${stop.key}-stop`}
            data-reached={reached.includes(stop.key)}
          >
            <h3 className={styles.head}>
              <span className={styles.number} aria-hidden="true">
                {index + 1}
              </span>
              <span className={styles.title}>{stop.title}</span>
              <span className={styles.time}>{stop.time}</span>
            </h3>
            <p className={styles.payoff}>
              {stop.key === 'room' ? roomPayoff(stop.payoff, storeTotal(lot)) : stop.payoff}
            </p>
            {tile(stop)}
            <a
              className={`${index === 0 ? pills.primary : pills.secondary} ${styles.open}`}
              href={stopAddress(stop.key, lot)}
              onClick={open(stop)}
              data-action={`review.stop.${stop.key}`}
              data-testid={`review-path-${stop.key}`}
            >
              {PATH.open}
              <span aria-hidden="true">→</span>
            </a>
          </li>
        ))}
      </ol>
    </section>
  );
}
