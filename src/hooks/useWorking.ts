import { useSyncExternalStore } from 'react';
import { working } from '../lib/working';

/**
 * Whether the site is working on something a person asked for and it has taken
 * longer than half a second (src/lib/working.ts). A page drawn on a server is never
 * locked, so the first draw in the browser matches it.
 */
export function useWorking(): boolean {
  return useSyncExternalStore(working.subscribe, working.locked, () => false);
}
