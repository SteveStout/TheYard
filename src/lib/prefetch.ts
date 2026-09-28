/**
 * Fetching code ahead of the click (ADR: Code that reads like code, addendum of
 * 28 September). The markdown renderer, the Admin tab and every Admin card are
 * chunks of their own so the first page does not pay for them; this fetches
 * them after the first page has loaded and the browser has nothing better to
 * do, so the click that needs one finds it already here. React-free, like the
 * rest of lib: the component names what to fetch and this decides when.
 */

/** What the browser says about the connection (`navigator.connection`), where it says anything. */
export type ConnectionHint = { saveData?: boolean; effectiveType?: string };

/**
 * Whether code nobody has asked for yet is worth this connection. Not when the
 * reader has asked the browser to save data, and not on a connection the
 * browser rates as 2G, where the bytes would compete with what is on screen.
 * A browser that says nothing about its connection gets the prefetch.
 */
export function shouldPrefetch(connection: ConnectionHint | undefined): boolean {
  if (connection === undefined) return true;
  if (connection.saveData === true) return false;
  return connection.effectiveType !== '2g' && connection.effectiveType !== 'slow-2g';
}

/** Runs its work later and returns a way to call it off. */
export type Scheduler = (work: () => void) => () => void;

/**
 * Once the page has finished loading, then once the browser is idle
 * (requestIdleCallback, or a short timer where a browser has none), so the
 * prefetch never competes with the first page's own bytes.
 */
export const afterLoadWhenIdle: Scheduler = (work) => {
  let cancelled = false;
  let idleId: number | undefined;
  let timer: number | undefined;
  const run = () => {
    if (!cancelled) work();
  };
  const idle = () => {
    if (cancelled) return;
    if (typeof window.requestIdleCallback === 'function') {
      idleId = window.requestIdleCallback(run, { timeout: 5000 });
    } else {
      timer = window.setTimeout(run, 1500);
    }
  };
  if (document.readyState === 'complete') idle();
  else window.addEventListener('load', idle, { once: true });
  return () => {
    cancelled = true;
    window.removeEventListener('load', idle);
    if (idleId !== undefined) window.cancelIdleCallback(idleId);
    if (timer !== undefined) window.clearTimeout(timer);
  };
};

/** The browser's word on the connection, where it gives one. */
export function browserConnection(): ConnectionHint | undefined {
  return (navigator as Navigator & { connection?: ConnectionHint }).connection;
}

/**
 * Fetches each chunk once the page is idle, unless the connection says not to.
 * A chunk that fails is left for the click to fetch again, so a failure here
 * costs nothing but the attempt.
 */
export function prefetchWhenIdle(
  loaders: ReadonlyArray<() => Promise<unknown>>,
  connection: ConnectionHint | undefined = browserConnection(),
  schedule: Scheduler = afterLoadWhenIdle
): () => void {
  if (!shouldPrefetch(connection) || loaders.length === 0) return () => {};
  return schedule(() => {
    for (const load of loaders) void load().catch(() => undefined);
  });
}
