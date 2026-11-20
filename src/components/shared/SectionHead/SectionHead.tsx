/**
 * Does:      Opens a section of a sales page the one way every section opens (ADR: The sales pages set the site's look):
 *            the gold number badge when the section has one ("01"), its heading on the left, where the reader is or a
 *            door on the right, and one line under when it has one.
 * Does not:  Keep the place or pick the words (the section hands them in), or draw the badge's look (sales.css does).
 * Used by:   ReviewPath.tsx, ReviewTour.tsx, ReviewVoices.tsx, ReviewWork.tsx, ReviewPage.tsx.
 */
import type { ReactNode } from 'react';
import styles from './SectionHead.module.css';

export function SectionHead({
  id,
  title,
  meta,
  hint,
  number,
}: {
  /** The heading's id, which the section is labelled by. */
  id: string;
  title: string;
  /** Where the reader is, or a door, on the right. */
  meta: ReactNode;
  /** The one line under the heading, if the section has one. */
  hint?: string;
  /** The section's place on the page, "01" to "06", drawn as the badge before the heading. */
  number?: string;
}) {
  return (
    <header>
      <div className={styles.head}>
        <h2 id={id} className={styles.title}>
          {number && (
            <span className="op-num" data-mock="section-num">
              {number}
            </span>
          )}
          {title}
        </h2>
        <p className={styles.meta}>{meta}</p>
      </div>
      {hint && <p className={styles.hint}>{hint}</p>}
    </header>
  );
}
