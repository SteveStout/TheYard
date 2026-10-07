/**
 * Does:      Draws the landing page to HTML, once, when the site is built, for index.html to carry.
 * Does not:  Run in a browser or on the server: the frontend build calls it through the Vite config's prerender step,
 *            and the API serves the page it wrote as a file and knows nothing about it.
 * Used by:   the frontend build only (no file in src imports it).
 */
// The edge build of React's renderer: it schedules its work with timers, where the browser build
// opens a message channel that keeps Node running after the draw is done, so the build never exits.
import { renderToString } from 'react-dom/server.edge';
import { TheYard } from './TheYard';

// #region draw-landing
/**
 * The landing page as HTML: the whole site drawn at the bare address, with no
 * window, nothing signed in, nothing fetched yet and the docking line not known.
 * Those are exactly the answers the browser's first draw gives while it takes
 * the page over (ADR: The landing page rendered at build time), which is what
 * lets it attach to this markup instead of replacing it. Effects never run here,
 * so nothing is asked of the API.
 */
export function drawLanding(): string {
  return renderToString(<TheYard />);
}
// #endregion draw-landing
