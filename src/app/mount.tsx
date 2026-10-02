/**
 * Does:      Draws the whole site into the page's one empty element, inside the safety net that catches a render crash.
 * Does not:  Decide what the site shows; App.tsx does that.
 * Used by:   main.tsx.
 */
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ErrorBoundary } from '../components/shared/ErrorBoundary';
import App from './App';

// #region mount
/**
 * Finds the element index.html leaves empty for the site and draws the site into it. Everything
 * the site shows lives under this one call.
 * @param elementId the id of that element, "root" in index.html
 */
export function mountTheYard(elementId: string): void {
  // A missing element would leave a blank page with no reason given; throwing names the problem.
  const root = document.getElementById(elementId);
  if (!root) throw new Error(`Missing #${elementId} element`);

  createRoot(root).render(
    // StrictMode is a development check. It renders nothing and costs nothing in production. In
    // development it mounts every component twice, so an effect that forgets to clean up (a
    // timer, a listener) shows its bug right away.
    <StrictMode>
      {/* The safety net. If a component throws while drawing, React would remove the whole page
          and leave it white. The boundary catches the throw, reports it to the API, and shows a
          card with Reload and Back to inventory instead. It also spots a chunk a deploy has
          replaced and reloads once onto the new version. */}
      <ErrorBoundary>
        {/* The whole site: routing, header, pages. */}
        <App />
      </ErrorBoundary>
    </StrictMode>
  );
}
// #endregion mount
