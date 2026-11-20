/**
 * Does:      Drives every motion on a sales page (ADR: The sales pages set the site's look): it watches one holder with an
 *            IntersectionObserver at a quarter in view and sets the class is-seen on it while it is, and clears it when
 *            it leaves, so each motion plays, three times by its own rule in sales.css, when its holder is first seen
 *            and again on return; watchSeen does the same for holders drawn as markup (a document's cards). With
 *            prefers-reduced-motion the class is never set and nothing moves. Before the
 *            holder is seen, and with JavaScript off, nothing has the class, so the finished state shows.
 * Does not:  Hold a motion itself (sales.css does, and comet.ts the one that runs the quality line) or decide what is a
 *            holder (each section passes its own).
 * Used by:   ReviewVerdict.tsx, ReviewWork.tsx, ReviewPath.tsx, ReviewVoices.tsx, ReviewTour.tsx, ReviewClose.tsx,
 *            LatestPosts.tsx, SectionHead.tsx, PulseBand.tsx, Landing.tsx, QualityFoot.tsx, useFirstSeen.test.ts.
 */
import { useEffect, useState } from 'react';

/** How much of a holder must be in the window for its motion to play. */
export const SEEN_AT = 0.25;

/** The class a seen holder carries; sales.css starts each motion under it. */
export const SEEN_CLASS = 'is-seen';

/** Whether a holder plays: a quarter of it in view, and the reader has not asked for less motion. */
export function seenNow(ratio: number, still: boolean): boolean {
  return !still && ratio >= SEEN_AT;
}

/**
 * Watches holders drawn outside React (a document's cards, which arrive as markup) the same way the hook below watches
 * one: each carries is-seen while a quarter of it is in view, never with reduced motion. Hand back the stop.
 */
export function watchSeen(holders: readonly Element[]): () => void {
  if (holders.length === 0 || typeof IntersectionObserver === 'undefined') return () => {};
  const still = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
  if (still) return () => {};
  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries)
        entry.target.classList.toggle(SEEN_CLASS, seenNow(entry.intersectionRatio, still));
    },
    { threshold: [0, SEEN_AT] }
  );
  for (const holder of holders) observer.observe(holder);
  return () => {
    observer.disconnect();
    for (const holder of holders) holder.classList.remove(SEEN_CLASS);
  };
}

/**
 * Watches a holder and keeps its is-seen class: hand the first value to the holder's ref; the second is true while it
 * is seen. A holder drawn later (a section that waits for its answer) is watched from when it is drawn.
 */
export function useFirstSeen<T extends HTMLElement>(): [(element: T | null) => void, boolean] {
  const [element, setElement] = useState<T | null>(null);
  const [seen, setSeen] = useState(false);
  useEffect(() => {
    if (!element || typeof IntersectionObserver === 'undefined') return;
    const still = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
    if (still) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        const now = seenNow(entry.intersectionRatio, still);
        element.classList.toggle(SEEN_CLASS, now);
        setSeen(now);
      },
      { threshold: [0, SEEN_AT] }
    );
    observer.observe(element);
    return () => {
      observer.disconnect();
      element.classList.remove(SEEN_CLASS);
    };
  }, [element]);
  return [setElement, seen];
}
