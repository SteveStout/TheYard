import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/fonts.css';
import './styles/tokens.css';
import './styles/operator.css';
import './styles/code.css';
import App from './App';
import { ErrorBoundary, reportClientError } from './components/ErrorBoundary';
import { captureAdminKey } from './lib/adminKey';
import { reloadOnce, tabStorage } from './lib/staleChunk';

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

// A chunk's own stylesheet or imports that a deploy has replaced fail before the
// chunk does, and Vite says so with this event: the page loads again onto the new
// version, once, and a second failure goes on to the boundary (src/lib/staleChunk.ts).
window.addEventListener('vite:preloadError', (event) => {
  if (reloadOnce(tabStorage(), () => window.location.reload())) event.preventDefault();
});

createRoot(root).render(
  <StrictMode>
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </StrictMode>
);
// #endregion bootstrap
