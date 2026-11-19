/**
 * Remembers whether the docked rail was collapsed, per browser, in local
 * storage. A missing or blocked store reads as open and a failed write is
 * dropped, so the rail never fails over a preference. Its own file because it
 * is storage, not drawing, and storage is src/lib's: useRail reads and writes it here.
 */

/** The local storage key the rail's collapsed state is kept under. */
const RAIL_KEY = 'theyard.rail';

/** What this browser keeps for the rail: 'collapsed', 'open', or null when nothing is kept or the store is blocked. */
export function readRailStored(): 'collapsed' | 'open' | null {
  try {
    const kept = window.localStorage.getItem(RAIL_KEY);
    return kept === 'collapsed' || kept === 'open' ? kept : null;
  } catch {
    return null;
  }
}

/**
 * Whether the rail starts collapsed: what this browser kept when it kept anything, otherwise the page's own default.
 * The review route's first screen defaults to collapsed (ADR: The review route gives a reason before it asks), and
 * that default is never written, so a visitor's own choice is the only thing ever kept.
 */
export function railStartsCollapsed(collapsedByDefault: boolean): boolean {
  const kept = readRailStored();
  return kept === null ? collapsedByDefault : kept === 'collapsed';
}

/** Keeps the rail's collapsed state for the next visit; a blocked store drops it. */
export function storeRailCollapsed(collapsed: boolean): void {
  try {
    window.localStorage.setItem(RAIL_KEY, collapsed ? 'collapsed' : 'open');
  } catch {
    // Private mode or a blocked store: the rail simply starts open next time.
  }
}
