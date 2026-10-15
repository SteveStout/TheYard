/**
 * Detail a card keeps behind one click: a closed fold whose summary says what
 * is inside it. Nothing a card drew before is gone; the long tables and the
 * operator's detail are folded so the numbers that answer the card's question
 * come first.
 */
import type { ReactNode } from 'react';
import styles from './card.module.css';

export function More({
  label,
  testId,
  children,
}: {
  /** What is in the fold, after the word More. */
  label: string;
  testId?: string;
  children: ReactNode;
}) {
  return (
    <details className={styles.about} data-testid={testId}>
      <summary className={styles.aboutSummary}>More: {label}</summary>
      {children}
    </details>
  );
}
