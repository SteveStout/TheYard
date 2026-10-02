/**
 * Remembers whether the docked rail was collapsed, per browser, in local
 * storage. A missing or blocked store reads as open and a failed write is
 * dropped, so the rail never fails over a preference. Its own file because it
 * is storage, not drawing: useRail reads and writes it through SideNav's index.
 */

/** The local storage key the rail's collapsed state is kept under. */
const RAIL_KEY = 'theyard.rail';

/** Whether the rail was left collapsed in this browser; false when nothing is stored or the store is blocked. */
export function readRailCollapsed(): boolean {
  try {
    return window.localStorage.getItem(RAIL_KEY) === 'collapsed';
  } catch {
    return false;
  }
}

/** Keeps the rail's collapsed state for the next visit; a blocked store drops it. */
export function storeRailCollapsed(collapsed: boolean): void {
  try {
    window.localStorage.setItem(RAIL_KEY, collapsed ? 'collapsed' : 'open');
  } catch {
    // Private mode or a blocked store: the rail simply starts open next time.
  }
}
