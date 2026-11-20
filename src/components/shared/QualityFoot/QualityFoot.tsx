/**
 * Does:      Draws the Quality sheet's foot (ADR-130, the addendum on the panel, the numbers and the foot): one last card
 *            after the numbered ones, the review route's closing question and its line beside the same three moves,
 *            Book 20 minutes, the gold Resume (PDF) and Message me on LinkedIn, each reporting the action its twin on
 *            the review route reports; under them Where next, its two links from data/hooks.json. Opened over the review
 *            route, "Back to the three questions" closes the sheet and takes the reader to section 02. While the sheet
 *            is open it plays the cards' motions (watchSeen): each badge's pass of light, the panel's live dot.
 * Does not:  Choose the links (the hooks file does), hold the question's words (reviewVerdict.ts does) or open the
 *            booking sheet (the email the address sends is the booking).
 * Used by:   DocDialog.tsx.
 */
import { useEffect, useRef, useSyncExternalStore, type MouseEvent } from 'react';
import { watchSeen } from '../../../hooks/useFirstSeen';
import { reportAction } from '../../../hooks/useReviewProgress';
import { holdCode, sessionOrNull, withCode } from '../../../lib/links';
import { nextFor } from '../../../lib/pageHooks';
import { QUESTIONS_ID } from '../../../lib/reviewCases';
import { CLOSE, CLOSE_MOVES } from '../../../lib/reviewVerdict';
import { plainClick, scrollToSection } from '../../../lib/sectionScroll';
import { PdfMark } from '../PdfMark';
import styles from './QualityFoot.module.css';

/** The held code never changes during a visit, so there is nothing to subscribe to. */
const noChange = () => () => {};
const heldCode = () => holdCode('', sessionOrNull());
const noCode = () => null;

/** The address of the review route's section 02, the foot's first link. */
const QUESTIONS_ADDRESS = `/?view=review#${QUESTIONS_ID}`;

export function QualityFoot({ onBack }: { onBack: () => void }) {
  const foot = useRef<HTMLElement>(null);
  const links = nextFor('quality');
  const code = useSyncExternalStore(noChange, heldCode, noCode);
  // The cards before the foot are markup the document drew; they are watched for their motions from here.
  useEffect(() => {
    const page = foot.current?.parentElement;
    if (!page || !foot.current) return;
    return watchSeen([
      ...page.querySelectorAll('[data-mock="q-card"], [data-mock="live-panel"]'),
      foot.current,
    ]);
  }, []);
  // Over the review route the first link closes the sheet and scrolls to the questions behind it.
  const back = (event: MouseEvent<HTMLAnchorElement>) => {
    if (!plainClick(event) || document.getElementById(QUESTIONS_ID) === null) return;
    event.preventDefault();
    onBack();
    const plain = {
      metaKey: false,
      ctrlKey: false,
      shiftKey: false,
      altKey: false,
      button: 0,
      preventDefault: () => {},
    };
    window.requestAnimationFrame(() => scrollToSection(plain, QUESTIONS_ID));
  };
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
      {links.length > 0 && (
        <nav
          className={styles.next}
          aria-label="Where next"
          data-mock="where-next"
          data-testid="where-next"
          data-next="quality"
        >
          <p className={styles.title}>Where next</p>
          <ul className={styles.links}>
            {links.map((link) => (
              <li key={link.href}>
                <a
                  href={withCode(link.href, code)}
                  onClick={link.href === QUESTIONS_ADDRESS ? back : undefined}
                  data-mock="next-link"
                >
                  {link.label}
                </a>
                <span className={styles.reason}>
                  {link.reason} <span className={styles.minutes}>{link.minutes} min</span>
                </span>
              </li>
            ))}
          </ul>
        </nav>
      )}
    </section>
  );
}
