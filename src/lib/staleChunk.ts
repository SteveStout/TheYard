/**
 * A page left open across a deploy (Steve's iPhone, 25 September, on the Admin
 * tab after 1.0.3.26 rolled: "Importing a module script failed" on every card
 * he opened). The page he had was 1.0.3.25's, which asks for its own chunks by
 * their hashed names, and the roll had replaced them with 1.0.3.26's. The one
 * move that helps is to load the page again, which fetches the new names; this
 * decides when that is the error, and reloads once. A second failure inside a
 * minute is not a stale page, so the error is shown rather than reloading in a
 * loop. No React in here.
 */

/** What each engine says when a chunk named by the page is not on the server any more. */
const STALE = [
  /Importing a module script failed/i, // WebKit
  /Failed to fetch dynamically imported module/i, // Chromium
  /error loading dynamically imported module/i, // Firefox
  /Unable to preload CSS/i, // Vite, for a chunk's stylesheet
];

export function isStaleChunk(error: unknown): boolean {
  const message = error instanceof Error ? error.message : typeof error === 'string' ? error : '';
  return STALE.some((pattern) => pattern.test(message));
}

/** The one key this writes, in the tab's own session storage. */
export const RELOADED_AT = 'theyard.stale-chunk-reload';

/** How long a reload counts as this one: a second failure inside it is shown, not reloaded. */
export const RELOAD_WINDOW_MS = 60_000;

type Storage = { getItem(key: string): string | null; setItem(key: string, value: string): void };

/**
 * Reloads the page onto the new version, once: true when it did (the caller
 * shows nothing more), false when a reload already happened inside the window
 * or there is no storage to remember it in (the caller shows the error).
 */
export function reloadOnce(
  storage: Storage | null,
  reload: () => void,
  now: number = Date.now()
): boolean {
  if (storage === null) return false;
  try {
    const last = Number(storage.getItem(RELOADED_AT));
    if (Number.isFinite(last) && last > 0 && now - last < RELOAD_WINDOW_MS) return false;
    storage.setItem(RELOADED_AT, String(now));
  } catch {
    return false;
  }
  reload();
  return true;
}

/** The tab's session storage, or null where the browser refuses it (a private window). */
export function tabStorage(): Storage | null {
  try {
    return window.sessionStorage;
  } catch {
    return null;
  }
}
