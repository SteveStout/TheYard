/**
 * Does:      Draws the review route at ?view=review as one unit, top to bottom in one list as the sales1120 picture draws
 *            it (ADR: The sales pages set the site's look): the first screen the address names (the verdict, or the
 *            mirror with its strip of names for ?for=ai), 01 the work's three case cards, 02 the short path's three
 *            questions, 03 the band of six named voices, 04 the blog's three newest posts, 05 the full tour of seven
 *            compact stops, 06 the close, and Where next, every section the page's one width; the booking sheet every
 *            "Book 20 minutes" opens, and on a phone the dock with the next step once the first screen has gone.
 * Does not:  Hold the stops (reviewStops.ts does), the words (reviewVerdict.ts and reviewCases.ts do) or the place
 *            (useReviewProgress.ts keeps it on the browser); the first screen's two answers are read by
 *            useReviewReadings.ts and the live panels' by usePulse.ts.
 * Used by:   App.tsx.
 */
import { useState, useSyncExternalStore } from 'react';
import RELEASES from 'virtual:release-facts';
import { usePulse } from '../../../hooks/usePulse';
import { useReviewProgress } from '../../../hooks/useReviewProgress';
import { useReviewPath } from '../../../hooks/useReviewPath';
import { useReviewReadings } from '../../../hooks/useReviewReadings';
import type { DocKey } from '../../../library/documents';
import { shareAddress } from '../../../lib/reviewBook';
import { asOfLine, rightNowRows, thisWeekRows } from '../../../lib/nowPanel';
import { centralDay, releasesInLastSevenDays } from '../../../lib/releaseFacts';
import {
  BOOK_ADDRESS,
  CLOSE,
  RESUME_ADDRESS,
  testsTile,
  VERDICT,
} from '../../../lib/reviewVerdict';
import { gateTotals, type ReviewScreen } from '../../../lib/reviewMirror';
import { shippedWords, storeTotal } from '../../../lib/reviewPath';
import { gateFacts } from '../../layout/StoreBar/gateFacts';
import { LatestPosts } from '../../landing/LatestPosts';
import { WhereNext } from '../../shared/WhereNext';
import { BookSheet } from '../BookSheet';
import { NameStrip } from '../NameStrip';
import { ReviewClose } from '../ReviewClose';
import { ReviewDock } from '../ReviewDock';
import { ReviewMirror } from '../ReviewMirror';
import { ReviewPath } from '../ReviewPath';
import { ReviewTour } from '../ReviewTour';
import { ReviewVerdict } from '../ReviewVerdict';
import { ReviewVoices } from '../ReviewVoices';
import { ReviewWork } from '../ReviewWork';
import { SectionHead } from '../../shared/SectionHead';
import styles from './ReviewPage.module.css';

/** What the build carries about this version, read once: gated, the quality numbers held and CodeRabbit's read. */
const GATE = gateFacts();
const QUALITY = GATE.quality === null ? null : { held: GATE.quality.held, of: GATE.quality.of };

/** The day never changes under a mounted page in a way worth a redraw, so nothing is subscribed (Landing.tsx's way). */
const noSubscription = () => () => {};
const visitorsDay = () => centralDay(new Date());
const buildsDay = () => RELEASES.builtOn;

export function ReviewPage({
  screen,
  now,
  onOpenDoc,
  onOpenBlog,
  onOpenSidebar,
}: {
  /** The first screen: the verdict, or the mirror for a link sent with for=ai. */
  screen: ReviewScreen;
  /** The page's clock, the one every countdown on the site reads. */
  now: number;
  onOpenDoc: (key: DocKey) => void;
  /** Opens the blog, at a post or at every post. */
  onOpenBlog: (slug: string | null) => void;
  /** Shows the sidebar, which the tour's last cell opens. */
  onOpenSidebar: () => void;
}) {
  const review = useReviewProgress(screen);
  const readings = useReviewReadings();
  const path = useReviewPath();
  const recommendations = readings.recommendations;
  // The age and the run link are this build's run's: drawn only when the summary read is of this same version.
  const sameRun = readings.summary?.version === GATE.version;
  const [booking, setBooking] = useState(false);
  const book = () => setBooking(true);
  // The live panels: one read of /api/pulse, and the week's releases counted as the home page counts them.
  const reading = usePulse();
  const asOf = asOfLine(reading);
  const today = useSyncExternalStore(noSubscription, visitorsDay, buildsDay);
  const releases = releasesInLastSevenDays(RELEASES.days, today);

  return (
    <>
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
              onBook={book}
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
            rightNow={rightNowRows(reading.pulse, now)}
            asOf={asOf}
            onAction={review.action}
            onBook={book}
          />
        )}
        <ReviewWork
          testsFigure={testsTile(readings.summary, QUALITY).figure}
          rows={thisWeekRows(reading.pulse, releases)}
          asOf={asOf}
          onAction={review.action}
          onOpenDoc={onOpenDoc}
          onOpenBlog={onOpenBlog}
        />
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
        <ReviewVoices
          recommendations={recommendations}
          onFollow={() => review.action('recommendations')}
        />
        <LatestPosts
          onOpenBlog={onOpenBlog}
          row={{
            head: (all) => (
              <SectionHead id="review-blog-title" number="04" title="From the blog" meta={all} />
            ),
            onFollow: () => review.action('blog'),
          }}
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
        <ReviewClose
          address={() =>
            shareAddress(window.location.origin, screen === 'mirror' ? 'ai' : null, review.code)
          }
          onAction={review.action}
          onBook={book}
          ask={CLOSE}
        />
        <div className={styles.next}>
          <WhereNext page="review" row />
        </div>
      </section>
      <ReviewDock
        key={screen}
        watch='[data-testid="review-proof"]'
        hidden={booking}
        moves={[
          {
            label: VERDICT.book,
            href: BOOK_ADDRESS,
            onClick: () => {
              review.action('book');
              book();
            },
          },
          {
            label: VERDICT.resume,
            href: RESUME_ADDRESS,
            onClick: () => review.action('resume'),
            resume: true,
          },
        ]}
      />
      <BookSheet open={booking} onClose={() => setBooking(false)} onAction={review.action} />
    </>
  );
}
