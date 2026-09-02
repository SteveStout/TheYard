import { Component, type ErrorInfo, type ReactNode } from 'react';
import styles from './ErrorBoundary.module.css';

// #region boundary
/**
 * The last line of defense for a render crash (ADR-023). React unmounts the
 * whole tree when a render throws, so without a boundary the visitor gets a
 * blank white page and no way back. This catches the throw, shows what
 * happened, and offers the two moves that actually help: reload, or go back
 * to the inventory with the failing query string dropped.
 *
 * It must be a class: `componentDidCatch` and `getDerivedStateFromError` have
 * no hook equivalent, and this is the one place in the app that needs them.
 * Errors thrown in event handlers and promises never reach a boundary, which
 * is why main.tsx also reports those (ADR: Error handling).
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state: { error: Error | null } = { error: null };

  /** Runs during the failed render: the only place state may be set from an error. */
  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  /** Runs after: side effects belong here, never in the method above. */
  componentDidCatch(error: Error, info: ErrorInfo) {
    reportClientError(error, info.componentStack ?? undefined);
  }

  render() {
    if (!this.state.error) {
      return this.props.children;
    }
    return (
      <div className={styles.wrap} role="alert" data-testid="render-error">
        <h1 className={styles.title}>Something went wrong on this page.</h1>
        <p className={styles.body}>
          The inventory is still running. Reload, or go back to the full list and try again.
        </p>
        <p className={styles.detail}>{this.state.error.message}</p>
        <div className={styles.actions}>
          <button type="button" className={styles.primary} onClick={() => window.location.reload()}>
            Reload the page
          </button>
          <button
            type="button"
            className={styles.secondary}
            onClick={() => {
              window.location.href = window.location.pathname;
            }}
          >
            Back to the inventory
          </button>
        </div>
      </div>
    );
  }
}
// #endregion boundary

// #region report
/**
 * Reports a browser-side error to the API, which records it in the same ring
 * buffer the Admin tab reads (ADR: Observability). Fire and forget on purpose:
 * a failed report must never replace the error the visitor is already seeing.
 */
export function reportClientError(error: unknown, componentStack?: string): void {
  const message = error instanceof Error ? error.message : String(error);
  const stack = error instanceof Error ? error.stack : undefined;
  try {
    void fetch('/api/errors/client', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // keepalive lets the report survive a navigation away from the page.
      keepalive: true,
      body: JSON.stringify({
        message,
        stack: (componentStack ?? stack ?? '').slice(0, 2000),
        path: window.location.pathname + window.location.search,
      }),
    }).catch(() => {});
  } catch {
    // A browser that refuses the request tells us nothing useful; stay quiet.
  }
}
// #endregion report
