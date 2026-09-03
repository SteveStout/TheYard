import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/tokens.css';
import App from './App';
import { ErrorBoundary, reportClientError } from './components/ErrorBoundary';

// #region bootstrap
// tokens.css is imported first so the palette exists before any component's
// styles are applied. StrictMode costs nothing in production; in development
// it mounts, unmounts and remounts once, which is how an effect that leaks a
// timer or a listener gets caught early.
const root = document.getElementById('root');
if (!root) throw new Error('Missing #root element');

// A boundary catches a crash during render. These two catch what a boundary
// never sees: a throw inside an event handler, and a promise nobody awaited.
// All three report to the API, so the Admin tab shows both sides of the app
// (ADR: Error handling).
window.addEventListener('error', (event) => reportClientError(event.error ?? event.message));
window.addEventListener('unhandledrejection', (event) => reportClientError(event.reason));

createRoot(root).render(
  <StrictMode>
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </StrictMode>
);
// #endregion bootstrap
