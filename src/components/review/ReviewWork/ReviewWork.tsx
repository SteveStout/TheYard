/**
 * Does:      Draws "01 The work" directly under the review route's first screen (ADR: The sales pages set the site's
 *            look), as the sales1120 picture draws it: the numbered head with its line and the door to the two-page
 *            resume at its right, then the three case cards, Conway, Storee and TheYard, each the anchor its
 *            first-screen tile's door goes to. TheYard's card takes the first screen's live tests figure and the week's
 *            live rows. The proof opens Quality in place, a post opens the blog in place, the questions door scrolls.
 * Does not:  Hold the words (reviewCases.ts does), read the API (the page hands the readings in) or draw the look
 *            (sales.css does).
 * Used by:   ReviewPage.tsx.
 */
import type { MouseEvent } from 'react';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import type { DocKey } from '../../../library/documents';
import { landOnSection } from '../../../library/pendingAnchor';
import type { NowRow } from '../../../lib/nowPanel';
import { QUESTIONS_ID, REVIEW_CASES, WORK, WORK_ID, type CaseDoor } from '../../../lib/reviewCases';
import type { ReviewAction } from '../../../lib/reviewProgress';
import { PROOF } from '../../../lib/reviewVerdict';
import { plainClick, scrollToSection } from '../../../lib/sectionScroll';
import { SectionHead } from '../../shared/SectionHead';
import { CaseCard } from '../CaseCard';
import styles from './ReviewWork.module.css';

export function ReviewWork({
  testsFigure,
  rows,
  asOf,
  onAction,
  onOpenDoc,
  onOpenBlog,
}: {
  /** The first screen's live tests figure (testsTile), the one TheYard's heading prints. */
  testsFigure: string;
  /** "TheYard, this week": the rows read live, each only when read. */
  rows: readonly NowRow[];
  /** "as of HH:MM" when the newest read of the pulse failed. */
  asOf: string | null;
  onAction: (action: ReviewAction) => void;
  onOpenDoc: (key: DocKey) => void;
  onOpenBlog: (slug: string | null) => void;
}) {
  // The holder of its number badge's light and TheYard panel's live dot.
  const [holder] = useFirstSeen<HTMLElement>();
  const door = (entry: CaseDoor, event: MouseEvent<HTMLAnchorElement>) => {
    if (entry.kind === 'questions') {
      scrollToSection(event, QUESTIONS_ID);
      return;
    }
    if (!plainClick(event)) return;
    if (entry.kind === 'proof') {
      // Opened in place at the top of Quality, so the page is not loaded again.
      event.preventDefault();
      landOnSection('');
      onOpenDoc(PROOF.doc);
    } else if (entry.kind === 'post') {
      event.preventDefault();
      onOpenBlog(entry.slug ?? null);
    }
  };
  return (
    <section
      ref={holder}
      id={WORK_ID}
      className={styles.work}
      aria-labelledby="review-work-title"
      data-mock="work"
      data-testid="review-work"
    >
      <SectionHead id="review-work-title" number="01" title={WORK.title} hint={WORK.hint} />
      <div className={styles.cases}>
        {REVIEW_CASES.map((entry) => (
          <CaseCard
            key={entry.id}
            entry={entry}
            testsFigure={testsFigure}
            rows={rows}
            asOf={asOf}
            onAction={onAction}
            onDoor={door}
          />
        ))}
      </div>
    </section>
  );
}
