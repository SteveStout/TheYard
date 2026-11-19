/**
 * Does:      Opens a section of the review route the one way every section opens (the whole-page picture's .head and
 *            .hint): its heading on the left, where the reader is on the right, and one line under.
 * Does not:  Keep the place or pick the words (the section hands them in).
 * Used by:   ReviewPath.tsx, ReviewTour.tsx.
 */
import type { ReactNode } from 'react';
import styles from './ReviewHead.module.css';

export function ReviewHead({
  id,
  title,
  meta,
  hint,
}: {
  /** The heading's id, which the section is labelled by. */
  id: string;
  title: string;
  /** Where the reader is, on the right. */
  meta: ReactNode;
  /** The one line under the heading. */
  hint: string;
}) {
  return (
    <header>
      <div className={styles.head}>
        <h2 id={id} className={styles.title}>
          {title}
        </h2>
        <p className={styles.meta}>{meta}</p>
      </div>
      <p className={styles.hint}>{hint}</p>
    </header>
  );
}
