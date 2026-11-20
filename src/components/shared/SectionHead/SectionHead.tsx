/**
 * Does:      Opens a section of a sales page the one way every section opens (ADR: The sales pages set the site's look):
 *            the gold number badge when the section has one ("01"), its heading on the left, where the reader is or a
 *            door on the right when it has one, and one line under when it has one. A head asked to be its own holder
 *            plays the badge's one pass of light when it is first seen (useFirstSeen), for a section that holds no
 *            other motion (the home page's).
 * Does not:  Keep the place or pick the words (the section hands them in), or draw the badge's look (sales.css does).
 * Used by:   ReviewPath.tsx, ReviewTour.tsx, ReviewVoices.tsx, ReviewWork.tsx, ReviewPage.tsx, Landing.tsx,
 *            LatestPosts.tsx.
 */
import type { ReactNode } from 'react';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import styles from './SectionHead.module.css';

export function SectionHead({
  id,
  title,
  meta,
  hint,
  number,
  seen = false,
}: {
  /** The heading's id, which the section is labelled by. */
  id: string;
  /** The heading's words, or a link that holds them (the home page's "What colleagues say"). */
  title: ReactNode;
  /** Where the reader is, or a door, on the right; none, nothing on the right. */
  meta?: ReactNode;
  /** The one line under the heading, if the section has one. */
  hint?: string;
  /** The section's place on the page, "01" to "06", drawn as the badge before the heading. */
  number?: string;
  /** The head is its own holder of the badge's pass of light, for a section with no holder of its own. */
  seen?: boolean;
}) {
  const [holder] = useFirstSeen<HTMLElement>();
  return (
    <header ref={seen ? holder : undefined}>
      <div className={styles.head}>
        <h2 id={id} className={styles.title}>
          {number && (
            <span className="op-num" data-mock="section-num">
              {number}
            </span>
          )}
          {title}
        </h2>
        {meta !== undefined && <p className={styles.meta}>{meta}</p>}
      </div>
      {hint && <p className={styles.hint}>{hint}</p>}
    </header>
  );
}
