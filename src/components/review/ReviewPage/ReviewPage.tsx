/**
 * Does:      Draws the review route at ?view=review as one unit, top to bottom as the whole-page picture draws it (ADR:
 *            The review route gives a reason before it asks, the addendum on one unit): the first screen the address
 *            names (the verdict, or the mirror with its strip of names for ?for=ai), the short path's three stops,
 *            the full tour of seven compact stops, the close, and Where next, every section the page's one width.
 * Does not:  Hold the stops (reviewStops.ts does), the words (reviewVerdict.ts does) or the place (useReviewProgress.ts
 *            keeps it on the browser); the first screen's two answers are read by useReviewReadings.ts.
 * Used by:   App.tsx.
 */
import { useReviewProgress } from '../../../hooks/useReviewProgress';
import { useReviewPath } from '../../../hooks/useReviewPath';
import { useReviewReadings } from '../../../hooks/useReviewReadings';
import type { DocKey } from '../../../library/documents';
import { gateTotals, type ReviewScreen } from '../../../lib/reviewMirror';
import { shippedWords, storeTotal } from '../../../lib/reviewPath';
import { gateFacts } from '../../layout/StoreBar/gateFacts';
import { WhereNext } from '../../shared/WhereNext';
import { NameStrip } from '../NameStrip';
import { ReviewClose } from '../ReviewClose';
import { ReviewMirror } from '../ReviewMirror';
import { ReviewPath } from '../ReviewPath';
import { ReviewTour } from '../ReviewTour';
import { ReviewVerdict } from '../ReviewVerdict';
import styles from './ReviewPage.module.css';

/** What the build carries about this version, read once: gated, the quality numbers held and CodeRabbit's read. */
const GATE = gateFacts();
const QUALITY = GATE.quality === null ? null : { held: GATE.quality.held, of: GATE.quality.of };

export function ReviewPage({
  screen,
  now,
  onOpenDoc,
  onOpenSidebar,
}: {
  /** The first screen: the verdict, or the mirror for a link sent with for=ai. */
  screen: ReviewScreen;
  /** The page's clock, the one every countdown on the site reads. */
  now: number;
  onOpenDoc: (key: DocKey) => void;
  /** Shows the sidebar, which the tour's last cell opens. */
  onOpenSidebar: () => void;
}) {
  const review = useReviewProgress(screen);
  const readings = useReviewReadings();
  const path = useReviewPath();
  const recommendations = readings.recommendations;
  // The age and the run link are this build's run's: drawn only when the summary read is of this same version.
  const sameRun = readings.summary?.version === GATE.version;

  return (
    <section className={styles.page} aria-labelledby="review-title" data-testid="review-page">
      {screen === 'mirror' ? (
        <>
          <ReviewMirror
            summary={readings.summary}
            gate={{
              gated: GATE.gated,
              version: GATE.version,
              quality: QUALITY,
              review: GATE.review,
            }}
            onAction={review.action}
            onOpenDoc={onOpenDoc}
          />
          <div className={styles.names}>
            <NameStrip
              recommendations={recommendations}
              onFollow={() => review.action('recommendations')}
            />
          </div>
        </>
      ) : (
        <ReviewVerdict
          summary={readings.summary}
          recommendations={recommendations}
          quality={QUALITY}
          onAction={review.action}
          onOpenDoc={onOpenDoc}
        />
      )}
      <ReviewPath
        reached={path.reached}
        readings={path.readings}
        lot={path.lot}
        gate={{
          gated: GATE.gated,
          quality: QUALITY,
          review: GATE.review,
          run: sameRun ? GATE.run : null,
        }}
        totals={gateTotals(readings.summary, GATE.version)}
        shipped={sameRun ? shippedWords(readings.summary?.ran_at, now) : null}
        now={now}
        onReach={path.reach}
        onAction={review.action}
        onOpenDoc={onOpenDoc}
      />
      <ReviewTour
        reached={review.reached}
        counts={{
          vehicles: storeTotal(path.lot),
          recommendations: recommendations === null ? '' : String(recommendations.length),
        }}
        onOpened={review.opened}
        onStartOver={review.startOver}
        onOpenSidebar={onOpenSidebar}
      />
      <ReviewClose recommendations={recommendations} onAction={review.action} />
      <div className={styles.next}>
        <WhereNext page="review" row />
      </div>
    </section>
  );
}
