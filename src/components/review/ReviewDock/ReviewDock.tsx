/**
 * Does:      Draws the phone's one next step on the review route (ADR: The review route gives a reason before it asks,
 *            the addendum on no dead ends), as the whole-page picture draws it: Book 20 minutes and Resume (PDF) fixed
 *            to the foot of a window under 640, off at load, on once the first screen's buttons have scrolled up out
 *            of the window and off again when they return; never drawn while the booking sheet is open. Its buttons
 *            are the first screen's own: the same addresses and the same actions, the resume the gold button. The home page hands it its own two,
 *            Check my work and Resume (PDF).
 * Does not:  Draw at all from 640 up (its sheet hides it) or open the sheet itself (the page does, on Book).
 * Used by:   ReviewPage.tsx, Landing.tsx.
 */
import { useEffect, useState, type MouseEvent } from 'react';
import { dockOn } from '../../../lib/reviewBook';
import { PdfMark } from '../../shared/PdfMark';
import styles from './ReviewDock.module.css';

/** One of the dock's two buttons: its words, its address, and what a press does before the browser follows it. */
export type DockMove = {
  label: string;
  href: string;
  onClick: (event: MouseEvent<HTMLAnchorElement>) => void;
  /** The resume: drawn as the gold button with its PDF page, as everywhere the resume is drawn. */
  resume?: boolean;
};

export function ReviewDock({
  watch,
  hidden,
  moves,
}: {
  /** The selector of the first screen's button the dock watches. */
  watch: string;
  /** The booking sheet is open: the dock is not drawn. */
  hidden: boolean;
  /** The filled button, then the outlined one. */
  moves: [DockMove, DockMove];
}) {
  const [on, setOn] = useState(false);
  useEffect(() => {
    const row = document.querySelector(watch);
    if (!row || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(([entry]) =>
      setOn(!entry.isIntersecting && dockOn(entry.boundingClientRect))
    );
    observer.observe(row);
    return () => observer.disconnect();
  }, [watch]);

  if (hidden) return null;
  return (
    <div
      className={styles.dock}
      data-on={on}
      inert={!on}
      aria-label="Next step"
      role="region"
      data-testid="review-dock"
    >
      {moves.map((move, index) => (
        <a
          key={move.label}
          className={`${move.resume ? 'op-pill-resume' : index === 0 ? 'op-pill-primary' : 'op-pill-secondary'} ${styles.pill}`}
          href={move.href}
          onClick={move.onClick}
          data-testid={move.resume ? 'dock-resume' : undefined}
        >
          {move.resume && <PdfMark />}
          {move.label}
        </a>
      ))}
    </div>
  );
}
