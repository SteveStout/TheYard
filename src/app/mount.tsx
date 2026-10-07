/**
 * Does:      Puts the site on the page: takes over the landing page the build already drew, or draws any other page from
 *            nothing.
 * Does not:  Decide what the site shows; App.tsx does that.
 * Used by:   main.tsx.
 */
import { createRoot, hydrateRoot } from 'react-dom/client';
import { TheYard } from './TheYard';

// #region mount
/**
 * Finds the element index.html holds for the site and puts the site in it.
 *
 * The build draws the landing page into that element (ADR: The landing page
 * rendered at build time), so on the bare address the page is already there
 * before this runs, and React takes it over with hydrateRoot: it draws the same
 * tree again, finds the same markup, and attaches the clicks to it without
 * replacing anything. Every other address (a document, the inventory, the
 * Admin tab) opens on another view, so the drawn landing page is not what it
 * wants; index.html keeps it hidden there, and this clears it and draws the
 * view from nothing with createRoot, as the site always did.
 * @param elementId the id of that element, "root" in index.html
 */
export function mountTheYard(elementId: string): void {
  // A missing element would leave a blank page with no reason given; throwing names the problem.
  const root = document.getElementById(elementId);
  if (!root) throw new Error(`Missing #${elementId} element`);

  const drawn = root.hasAttribute('data-drawn');
  root.removeAttribute('data-drawn');
  if (drawn && window.location.search === '') {
    hydrateRoot(root, <TheYard />);
    return;
  }
  root.replaceChildren();
  createRoot(root).render(<TheYard />);
}
// #endregion mount
