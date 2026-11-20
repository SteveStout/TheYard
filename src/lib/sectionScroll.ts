/**
 * Does:      Takes a reader to a section of the page they are on (ADR: The sales pages set the site's look): a plain click
 *            on a door whose address is "#<id>" scrolls to that section, smoothly unless the reader asked for reduced
 *            motion, with the room the sections' scroll-margin-top leaves clear, and the address does not change, so the
 *            page keeps its view and any r= code. A modified click keeps the browser's own meaning.
 * Does not:  Know which sections a page has (the door names one) or draw anything; no React in here.
 * Used by:   ReviewVerdict.tsx, ReviewWork.tsx, sectionScroll.test.ts.
 */

/** The parts of a click this file reads, so a test can hand it a plain object. */
export type ClickLike = {
  metaKey: boolean;
  ctrlKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
  button: number;
  preventDefault: () => void;
};

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
export function plainClick(event: ClickLike): boolean {
  return !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);
}

/**
 * Scrolls to the section named by id on a plain click and keeps the address; true when it did. A section the page has
 * not drawn leaves the click to the browser, which goes to the address's anchor as any link does.
 */
export function scrollToSection(event: ClickLike, id: string, doc: Document = document): boolean {
  if (!plainClick(event)) return false;
  const target = doc.getElementById(id);
  if (target === null) return false;
  event.preventDefault();
  const still = doc.defaultView?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
  target.scrollIntoView({ behavior: still ? 'auto' : 'smooth', block: 'start' });
  return true;
}
