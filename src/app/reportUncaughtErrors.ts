/**
 * Does:      Reports the two kinds of crash an error boundary never sees: a throw inside an event handler, and a promise nobody awaited.
 * Does not:  Catch a crash during render; ErrorBoundary does that, and all three report to the same place.
 * Used by:   main.tsx.
 */
import { reportClientError } from '../components/shared/ErrorBoundary';

// #region window-handlers
/**
 * Listens on the window for errors and rejected promises and sends each one to the API, so the
 * Admin tab shows the browser's failures beside the server's (ADR: Error handling).
 */
export function reportUncaughtErrors(): void {
  // A throw inside an event handler, a timer or a script that is not React rendering.
  window.addEventListener('error', (event) => reportClientError(event.error ?? event.message));
  // A promise that failed with nothing waiting on it.
  window.addEventListener('unhandledrejection', (event) => reportClientError(event.reason));
}
// #endregion window-handlers
