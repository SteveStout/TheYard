/**
 * Does:      Draws About Steven as a page of its own at ?view=about (ADR: The sidebar, the addendum on About Steven as a
 *            page), in the recommendations page's column and in the order a hiring manager reads: the eyebrow; the hero
 *            from the author document, its buttons the gold Resume (PDF), Book 20 minutes and the document's LinkedIn
 *            and GitHub; the review route's three proof tiles, each door leaving for its case card; How I lead; the six
 *            voices and the link to all of them; the site's one close; Away from the keyboard, every block; the credit.
 *            Book 20 minutes opens the booking sheet beside the email app, as the recommendations page's does.
 * Does not:  Hold a word of its own beyond its eyebrow (the document, reviewVerdict.ts and the review parts do), read the
 *            API (useAuthorParts.ts and useReviewReadings.ts do), or draw the document's colleagues column or its own
 *            closing panel: a page has one ask, and the voices lead to all the recommendations.
 * Used by:   App.tsx.
 */
import { useState } from 'react';
import { useAuthorParts } from '../../../hooks/useAuthorParts';
import { useReviewReadings } from '../../../hooks/useReviewReadings';
import { ABOUT_ADDRESS, ABOUT_EYEBROW, heroOf, titleOf } from '../../../lib/aboutPage';
import {
  BOOK_ADDRESS,
  CLOSE,
  RECORD_TILES,
  RESUME_ADDRESS,
  testsTile,
  VERDICT,
} from '../../../lib/reviewVerdict';
import { gateFacts } from '../../layout/StoreBar/gateFacts';
import { BookSheet } from '../../review/BookSheet';
import { ReviewClose } from '../../review/ReviewClose';
import { PdfMark } from '../../shared/PdfMark';
import { ProofTile } from '../../shared/ProofTile';
import { ReviewVoices } from '../../shared/ReviewVoices';
import prose from '../../../library/DocProse.module.css';
import panels from '../../../library/DocPanels.module.css';
import author from '../../../library/AuthorPage.module.css';
import styles from './AboutPage.module.css';

/** The quality numbers this build held, as the review route's live tile reads them. */
const GATE = gateFacts();
const QUALITY = GATE.quality === null ? null : { held: GATE.quality.held, of: GATE.quality.of };

/** The document's sheets the author page needs, as the window gave them, less the colleagues column's. */
const PROSE = `${prose.prose} ${panels.prose} ${author.prose}`;

/** A press on a part that counts itself by its address: nothing more to report. */
const counted = () => {};

/** A profile link's name in a press's data-action: linkedin, github. */
const actionOf = (label: string) => label.toLowerCase().replace(/[^a-z]+/g, '');

export function AboutPage() {
  const parts = useAuthorParts();
  const readings = useReviewReadings();
  const [booking, setBooking] = useState(false);
  const book = () => setBooking(true);
  const ready = parts !== null && parts !== 'failed' ? parts : null;
  const hero = ready ? heroOf(ready.intro) : null;
  const [lead, ...away] = ready?.panels ?? [];
  const tiles = [...RECORD_TILES, testsTile(readings.summary, QUALITY)];

  // The booking sheet stands beside the page, as the review route's does, so the page holds no window's close.
  return (
    <>
      <section
        className={`${styles.page} ${panels.page}`}
        aria-labelledby="about-title"
        aria-busy={parts === null}
        data-testid="about-page"
        data-mock="about-page"
      >
        <p className={styles.eyebrow} data-mock="eyebrow">
          {ABOUT_EYEBROW}
        </p>
        {parts === 'failed' && (
          <p className={styles.failed}>
            Couldn&rsquo;t load the About Steven - is the API running?
          </p>
        )}
        <div className={PROSE} data-doc="author">
          <div
            className={`author-page ${styles.flow}`}
            data-testid="author-page"
            data-mock="author-page"
          >
            <h1 id="about-title">{titleOf(ready?.top ?? '')}</h1>
            {hero && (
              <section className="author-panel author-intro op-glass" data-mock="intro">
                <div className="author-hero">
                  <div className="author-hero-words">
                    <div
                      className={styles.contents}
                      dangerouslySetInnerHTML={{ __html: hero.words }}
                    />
                    <p className={`author-buttons ${styles.moves}`} data-mock="hero-moves">
                      <a
                        className="op-pill-resume"
                        href={RESUME_ADDRESS}
                        data-action="about.resume"
                        data-testid="about-resume"
                      >
                        <PdfMark />
                        {VERDICT.resume}
                      </a>
                      <a
                        className={`op-pill-secondary ${styles.book}`}
                        href={BOOK_ADDRESS}
                        onClick={book}
                        data-action="about.book"
                        data-testid="about-book"
                      >
                        {VERDICT.book}
                      </a>
                      {hero.links.map((link) => (
                        <a
                          key={link.href}
                          className="author-button"
                          href={link.href}
                          target="_blank"
                          rel="noopener"
                          data-action={`about.${actionOf(link.label)}`}
                        >
                          {link.label}
                        </a>
                      ))}
                    </p>
                  </div>
                  {hero.photo && (
                    <div
                      className={styles.photo}
                      dangerouslySetInnerHTML={{ __html: hero.photo }}
                    />
                  )}
                </div>
              </section>
            )}
            {ready && (
              <ul className={styles.proof} aria-label="The proof" data-mock="proof">
                {tiles.map((tile) => (
                  <ProofTile
                    key={tile.key}
                    tile={tile}
                    onDoor={counted}
                    leaves
                    action="about.work"
                  />
                ))}
              </ul>
            )}
            {lead !== undefined && (
              <section
                className={`author-panel op-glass ${styles.lead}`}
                data-mock="lead"
                dangerouslySetInnerHTML={{ __html: lead }}
              />
            )}
            {ready && (
              <ReviewVoices
                recommendations={readings.recommendations}
                onFollow={counted}
                number={null}
                action="about.recommendations"
              />
            )}
            {ready && (
              <ReviewClose
                address={() => `${window.location.origin}${ABOUT_ADDRESS}`}
                onAction={counted}
                onBook={book}
                ask={{ question: CLOSE.question, line: CLOSE.line }}
                action="about"
              />
            )}
            {away.map((panel, index) => (
              <section
                key={index}
                className={`author-panel op-glass ${styles.away}`}
                data-mock={index === 0 ? 'away' : undefined}
                dangerouslySetInnerHTML={{ __html: panel }}
              />
            ))}
            {ready?.credit && (
              <footer
                className="author-credit"
                dangerouslySetInnerHTML={{ __html: ready.credit }}
              />
            )}
          </div>
        </div>
      </section>
      <BookSheet open={booking} onClose={() => setBooking(false)} onAction={counted} />
    </>
  );
}
