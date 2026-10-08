import { useCallback, useSyncExternalStore } from 'react';

/**
 * True while the window matches a CSS media query; follows resizes.
 *
 * A server has no window, so a page drawn there is drawn as if the query did
 * not match, and the browser takes it over with that same answer before it
 * asks the window: useSyncExternalStore reads the third function while it
 * hydrates and the second one after (ADR: A rendering service beside the API).
 * @param query the media query, as a stylesheet writes it
 */
export function useMediaQuery(query: string): boolean {
  const watch = useCallback(
    (onChange: () => void) => {
      const list = window.matchMedia(query);
      list.addEventListener('change', onChange);
      return () => list.removeEventListener('change', onChange);
    },
    [query]
  );
  return useSyncExternalStore(
    watch,
    () => window.matchMedia(query).matches,
    () => false
  );
}
