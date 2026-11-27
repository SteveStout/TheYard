/**
 * Does:      Draws About Steven as a page of its own at ?view=about in the review route's look (ADR: The sidebar, the
 *            addendums on About Steven as a page and on the person beside the work; ADR-142, the addendum on the landing
 *            views), in the order aboutOrder.ts lists: the shared page head (the eyebrow, "Hi, I'm Steve." over the gold
 *            rule, the role line and his two paragraphs, the three moves, the roles row, and his and Katie's photo as
 *            the side, straight after the title on a phone); then How I lead, the work and the voices numbered 01 to 03
 *            with the personal rows between them, the coaching card, Why I write, the couple's photographs and the one
 *            close, numbered 04. Book 20 minutes opens the booking sheet beside the email app.
 * Does not:  Hold a word of its own beyond its eyebrow (the document, reviewVerdict.ts and the review parts do), read the
 *            API (useAuthorParts.ts and useReviewReadings.ts do), choose the order or a row's side (aboutOrder.ts does),
 *            or draw the document's colleagues column or its own closing panel: a page has one ask.
 * Used by:   App.tsx.
 */
import { Fragment, useState, type ReactNode } from 'react';
import { useAuthorParts } from '../../../hooks/useAuthorParts';
import { useReviewReadings } from '../../../hooks/useReviewReadings';
import { placed, VOICES_FIRST, type AboutPart } from '../../../lib/aboutOrder';
import { ABOUT_ADDRESS, ABOUT_EYEBROW, heroOf, titleOf } from '../../../lib/aboutPage';
import { weave } from '../../../lib/aboutWeave';
import { MOVES } from '../../../lib/landingMoves';
import { WORK } from '../../../lib/reviewCases';
import { REVIEW_ADDRESS } from '../../../lib/reviewStops';
import {
  BOOK_ADDRESS,
  CLOSE,
  RECORD_TILES,
  RESUME_ADDRESS,
  testsTile,
  VERDICT,
} from '../../../lib/reviewVerdict';
import { gateFacts } from '../../layout/StoreBar/gateFacts';
import { AboutLead } from '../AboutLead';
import { AboutRow } from '../AboutRow';
import { AboutWrite } from '../AboutWrite';
import { BookSheet } from '../../review/BookSheet';
import { ReviewClose } from '../../review/ReviewClose';
import { PageHead } from '../../shared/PageHead';
import { PdfMark } from '../../shared/PdfMark';
import { ProofTile } from '../../shared/ProofTile';
import { ReviewVoices } from '../../shared/ReviewVoices';
import { SectionHead } from '../../shared/SectionHead';
import prose from '../../../library/DocProse.module.css';
import author from '../../../library/AuthorPage.module.css';
import styles from './AboutPage.module.css';

/** The quality numbers this build held, as the review route's live tile reads them. */
const GATE = gateFacts();
const QUALITY = GATE.quality === null ? null : { held: GATE.quality.held, of: GATE.quality.of };

/** The document's sheets a photograph's frame needs. */
const PROSE = `${prose.prose} ${author.prose}`;

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
  const woven = ready ? weave(ready.panels, ready.credit) : null;
  const tiles = [...RECORD_TILES, testsTile(readings.summary, QUALITY)];
  const away = woven?.away ?? null;
  const label = away?.label ?? '';

  // #region head
  const head = (
    <PageHead
      key="head"
      eyebrow={ABOUT_EYEBROW}
      title={hero?.title || titleOf(ready?.top ?? '')}
      titleId="about-title"
      sideOnPhone="early"
      sideLabel="Steve and Katie"
      side={
        hero?.photo && (
          <div
            className={`${PROSE} op-glass ${styles.photo}`}
            data-mock="hero-photo"
            dangerouslySetInnerHTML={{ __html: hero.photo }}
          />
        )
      }
    >
      {parts === 'failed' && (
        <p className={styles.failed}>Couldn&rsquo;t load the About Steven - is the API running?</p>
      )}
      {hero && <div className={styles.words} dangerouslySetInnerHTML={{ __html: hero.words }} />}
      <div className={styles.moves} data-mock="ctas">
        <a
          className={`op-pill-primary ${styles.primary}`}
          href={REVIEW_ADDRESS}
          data-action="about.review"
          data-mock="cta-primary"
          data-testid="about-review"
        >
          {MOVES.primary}
          <span className="op-nudge-x" aria-hidden="true">
            →
          </span>
        </a>
        <a
          className="op-pill-resume"
          href={RESUME_ADDRESS}
          data-action="about.resume"
          data-mock="cta-resume"
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
          data-mock="cta-book"
          data-testid="about-book"
        >
          {VERDICT.book}
        </a>
      </div>
      {hero && (
        <section
          className={`${styles.roles} op-glass op-roles-row`}
          aria-label="Roles"
          data-mock="strip"
        >
          <p className={styles.rolesLine}>{VERDICT.roles}</p>
          <ul className={styles.roleLinks}>
            {[...hero.links, { href: '/?view=blog', label: 'Blog' }].map((link) => (
              <li key={link.href}>
                <a
                  className="op-pill-secondary"
                  href={link.href}
                  {...(link.href.startsWith('http') ? { target: '_blank', rel: 'noopener' } : {})}
                  data-action={`about.${actionOf(link.label)}`}
                >
                  {link.label}
                </a>
              </li>
            ))}
          </ul>
        </section>
      )}
    </PageHead>
  );
  // #endregion head

  // #region parts
  // Each part of the page by its name in aboutOrder.ts; one the document has nothing for draws nothing.
  const part = (name: AboutPart): ReactNode => {
    switch (name) {
      case 'head':
        return head;
      case 'lead':
        return (
          woven?.lead && (
            <AboutLead key={name} lead={woven.lead} label={label} number="01" part="section" />
          )
        );
      case 'work':
        return (
          ready && (
            <section key={name} aria-labelledby="about-work-title" data-mock="section">
              <SectionHead id="about-work-title" number="02" title={WORK.title} hint={WORK.hint} />
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
            </section>
          )
        );
      case 'voices':
      case 'voices-more':
        return (
          ready && (
            <ReviewVoices
              key={name}
              recommendations={readings.recommendations}
              onFollow={counted}
              number="03"
              action="about.recommendations"
              {...(name === 'voices' ? { to: VOICES_FIRST } : { from: VOICES_FIRST, bare: true })}
            />
          )
        );
      case 'coaching':
        return (
          woven?.lead && (
            <AboutLead key={name} lead={woven.lead} label={label} number="01" part="coaching" />
          )
        );
      case 'write':
        return woven?.write && <AboutWrite key={name} write={woven.write} />;
      case 'strip':
        return away?.strip && <AboutRow key={name} strip={away.strip} />;
      case 'close':
        // A later panel the document adds, and its small print when no strip holds it, stand before the one close.
        return (
          <Fragment key={name}>
            {woven?.more.map((panel, index) => (
              <section
                key={index}
                className={`${PROSE} author-panel op-glass`}
                dangerouslySetInnerHTML={{ __html: panel }}
              />
            ))}
            {woven?.credit && (
              <footer
                className={`${PROSE} author-credit`}
                dangerouslySetInnerHTML={{ __html: woven.credit }}
              />
            )}
            {ready && (
              <ReviewClose
                address={() => `${window.location.origin}${ABOUT_ADDRESS}`}
                onAction={counted}
                onBook={book}
                ask={{ number: '04', question: CLOSE.question, line: CLOSE.line }}
                action="about"
              />
            )}
          </Fragment>
        );
    }
  };
  // #endregion parts

  const order = placed(away?.cards.map((card) => card.title) ?? []);
  // The booking sheet stands beside the page, as the review route's does, so the page holds no window's close.
  return (
    <>
      <section
        className={styles.page}
        aria-labelledby="about-title"
        aria-busy={parts === null}
        data-testid="about-page"
        data-mock="about-page"
      >
        {order.map((entry) => {
          if (!('row' in entry)) return part(entry.part);
          const card = away?.cards.find((one) => one.title === entry.row);
          return card && <AboutRow key={entry.row} card={card} side={entry.side} label={label} />;
        })}
      </section>
      <BookSheet open={booking} onClose={() => setBooking(false)} onAction={counted} />
    </>
  );
}
