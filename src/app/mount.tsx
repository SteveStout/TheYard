/**
 * Does:      Puts the site on the page: takes over a page the build or the rendering service already drew, or draws any
 *            other page from nothing.
 * Does not:  Decide what the site shows; App.tsx does that.
 * Used by:   main.tsx.
 */
import { createRoot, hydrateRoot } from 'react-dom/client';
import { readFirstLoad } from './firstLoad';
import { TheYard } from './TheYard';

// #region mount
/**
 * Finds the element index.html holds for the site and puts the site in it.
 *
 * Two kinds of page arrive already drawn. The rendering service draws the page
 * for any address and writes what it read beside it (ADR: A rendering service
 * beside the API); React takes that over with hydrateRoot from the same answers.
 * The build draws the landing page into index.html (ADR: The landing page
 * rendered at build time); on the bare address React takes that over the same
 * way. Every other address on a page the service did not draw opens on another
 * view, so the drawn landing page is not what it wants; index.html keeps it
 * hidden there, and this clears it and draws the view from nothing with
 * createRoot, as the site always did.
 * @param elementId the id of that element, "root" in index.html
 */
export function mountTheYard(elementId: string): void {
  // A missing element would leave a blank page with no reason given; throwing names the problem.
  const root = document.getElementById(elementId);
  if (!root) throw new Error(`Missing #${elementId} element`);

  const drawn = root.getAttribute('data-drawn');
  root.removeAttribute('data-drawn');
  const firstLoad = drawn === 'server' ? readFirstLoad() : null;
  if (firstLoad && firstLoad.search === window.location.search.replace(/^\?/, '')) {
    hydrateRoot(root, <TheYard firstLoad={firstLoad} />);
    return;
  }
  if (drawn === 'landing' && window.location.search === '') {
    hydrateRoot(root, <TheYard />);
    return;
  }
  root.replaceChildren();
  createRoot(root).render(<TheYard />);
}
// #endregion mount
