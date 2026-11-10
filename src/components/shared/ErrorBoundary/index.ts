/**
 * Does:      The folder entry for ErrorBoundary: re-exports ErrorBoundary, so an import names the folder,
 *            components/shared/ErrorBoundary.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   TheYard.tsx, reportUncaughtErrors.ts.
 */
export * from './ErrorBoundary';
