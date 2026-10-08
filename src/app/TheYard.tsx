/**
 * Does:      Holds the whole site as one element: the development checks, the safety net for a render crash, and the app.
 * Does not:  Put it on a page; mount.tsx does that in the browser, drawLanding.tsx draws it to HTML for the build, and
 *            entry-server.tsx draws it for the rendering service.
 * Used by:   mount.tsx, drawLanding.tsx, entry-server.tsx.
 */
import { StrictMode } from 'react';
import { ErrorBoundary } from '../components/shared/ErrorBoundary';
import { FirstLoadContext, type FirstLoad } from '../hooks/useFirstLoad';
import App from './App';

// #region the-site
/**
 * The site, the same element in every place it is drawn. The build draws it to
 * HTML for the landing page, the rendering service draws it for any address, and
 * the browser takes that HTML over by drawing it again; the draws must produce the
 * same markup, so they start from one tree and, on a page the service drew, from
 * the same first load (ADR: A rendering service beside the API).
 */
export function TheYard({ firstLoad = null }: { firstLoad?: FirstLoad | null }) {
  return (
    // StrictMode is a development check. It renders nothing and costs nothing in production. In
    // development it mounts every component twice, so an effect that forgets to clean up (a
    // timer, a listener) shows its bug right away.
    <StrictMode>
      {/* The safety net. If a component throws while drawing, React would remove the whole page
          and leave it white. The boundary catches the throw, reports it to the API, and shows a
          card with Reload and Back to inventory instead. It also spots a chunk a deploy has
          replaced and reloads once onto the new version. */}
      <ErrorBoundary>
        {/* What the rendering service read before it drew the page, for the first draw only. */}
        <FirstLoadContext.Provider value={firstLoad}>
          {/* The whole site: routing, header, pages. */}
          <App />
        </FirstLoadContext.Provider>
      </ErrorBoundary>
    </StrictMode>
  );
}
// #endregion the-site
