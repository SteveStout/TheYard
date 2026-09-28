import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/fonts.css';
import './styles/colors.css';
import './styles/sizes.css';
import './styles/typography.css';
import './styles/effects.css';
import './styles/base.css';
import './styles/panels.css';
import './styles/code-highlight.css';
import App from './app/App';
import { ErrorBoundary, reportClientError } from './components/shared/ErrorBoundary';
import { captureAdminKey } from './lib/adminKey';

// #region bootstrap
// The stylesheets load in the order of the imports above, and the order
// matters. fonts.css declares the face before anything asks for it. colors,
// sizes, typography and effects define every design value as a variable,
// each file named for what it controls. base.css sets the page's defaults
// from those variables. panels.css and code-highlight.css draw shared looks
// in the same variables, so they come after them.
const root = document.getElementById('root');
if (!root) throw new Error('Missing #root element');

// Read the operator's key now, before the first render removes it from the
// address bar. The Admin tab that needs it loads later, in its own chunk.
captureAdminKey();

// A boundary catches a crash during render. These two catch what a boundary
// never sees: a throw inside an event handler, and a promise nobody awaited.
// All three report to the API, so the Admin tab shows both sides of the app
// (ADR: Error handling).
window.addEventListener('error', (event) => reportClientError(event.error ?? event.message));
window.addEventListener('unhandledrejection', (event) => reportClientError(event.reason));

// Mount the app into <div id="root"> in index.html. Everything the site
// draws lives under this one call.
createRoot(root).render(
  // StrictMode is a development check. It renders nothing and costs nothing
  // in production. In dev it mounts every component twice, so an effect that
  // forgets to clean up (a timer, a listener) shows its bug right away.
  <StrictMode>
    {/* The safety net. If any component under it throws while rendering,
        React would normally unmount the whole tree and leave a blank white
        page. The boundary catches the throw, reports it to the API, and shows
        a card with Reload and Back to inventory instead. It also spots a
        chunk that a deploy has replaced and reloads once onto the new version.

        What it cannot catch: errors inside event handlers or promises. Those
        never reach a boundary, which is why the two window listeners above
        report them. */}
    <ErrorBoundary>
      {/* The whole site: routing, header, pages. */}
      <App />
    </ErrorBoundary>
  </StrictMode>
);
// #endregion bootstrap
