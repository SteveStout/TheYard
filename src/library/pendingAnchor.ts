/**
 * Does:      Holds the section the next document drawn lands on: the first address's hash, read before the address bar
 *            is rewritten without it, or a section a link asked for, such as the review route's mirror opening Quality in
 *            place at "The slip, and the turn"; cleared when a link opens a document at its top.
 * Does not:  Scroll or draw anything (useLiveBlocks.tsx lands on it once the document is drawn), or read a document.
 * Used by:   ReviewMirror.tsx, ReviewPath.tsx, ReviewVerdict.tsx, useLiveBlocks.tsx, pendingAnchor.test.ts.
 */

let pending =
  typeof window === 'undefined' ? '' : decodeURIComponent(window.location.hash.slice(1));

/** The section waiting to be landed on, "" for none. */
export function pendingAnchor(): string {
  return pending;
}

/** Asks the next document drawn to land on a section by its id; "" clears it once it has been landed on. */
export function landOnSection(anchor: string): void {
  pending = anchor;
}
