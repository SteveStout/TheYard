import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/fonts.css';
import './styles/tokens.css';
import './styles/operator.css';
import './styles/code.css';
import App from './App';
import { ErrorBoundary, reportClientError } from './components/ErrorBoundary';
import { captureAdminKey } from './lib/adminKey';

// #region bootstrap
// fonts.css comes first so the face is declared before anything asks
// for them, then tokens.css so the palette exists before any component's
// styles are applied, operator.css with the shared shapes drawn in those
// tokens, and code.css after them, because the code theme is written in the
// tokens too. StrictMode costs nothing in production; in development
// it mounts, unmounts and remounts once, which is how an effect that leaks a
// timer or a listener gets caught early.
const root = document.getElementById('root');
if (!root) throw new Error('Missing #root element');

// The operator's key is read here, before the first render takes it out of the
// address bar, because the Admin tab that uses it now arrives in a chunk of its
// own and mounts after that (1.0.3.0).
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
