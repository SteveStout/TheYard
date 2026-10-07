/**
 * Does:      Says how the sidebar sits: docked open, docked collapsed, a drawer on a narrow screen, or not known yet
 *            while a page drawn at build time is taken over, and remembers a collapsed rail between visits.
 * Does not:  Draw the sidebar (SideNav does) or choose what is in it.
 * Used by:   App.tsx, Shell.tsx.
 */
import { useEffect, useState, useSyncExternalStore } from 'react';
import { readRailCollapsed, storeRailCollapsed } from '../../components/layout/SideNav';
import { DESK } from '../../lib/breakpoints';

export type Rail = ReturnType<typeof useRail>;

// #region known-after-hydration
// The landing page arrives drawn at build time (ADR: The landing page rendered
// at build time), and the build cannot know the reader's window or what their
// browser remembers. React takes that page over by drawing it again with the
// same answers the build gave, which here is "not known yet" for the docking
// line and "open" for the rail, then asks the browser at once and draws again.
// useSyncExternalStore is React's way of saying exactly that: the third
// argument is the build's answer, used only while taking the page over; on any
// other page the browser is asked from the first draw, as it always was.
const whileTakingOver = () => null;
const openWhileTakingOver = () => false;
const nothingToWatch = () => () => {};

function watchTheDockingLine(onChange: () => void): () => void {
  const list = window.matchMedia(DESK);
  list.addEventListener('change', onChange);
  return () => list.removeEventListener('change', onChange);
}
const pastTheDockingLine = () => window.matchMedia(DESK).matches;
// #endregion known-after-hydration

export function useRail() {
  // #region docking
  /** The sidebar (ADR-013): a docked rail at 1024px and up, a drawer below, null while a drawn page is taken over. */
  const docked: boolean | null = useSyncExternalStore(
    watchTheDockingLine,
    pastTheDockingLine,
    whileTakingOver
  );
  // What this browser remembers, until the reader chooses again on this visit.
  const remembered = useSyncExternalStore(nothingToWatch, readRailCollapsed, openWhileTakingOver);
  const [chosen, setChosen] = useState<boolean | null>(null);
  const railCollapsed = chosen ?? remembered;
  const [drawerOpen, setDrawerOpen] = useState(false);
  // Remember a collapsed rail between visits: only a choice made on this visit is written.
  useEffect(() => {
    if (chosen !== null) storeRailCollapsed(chosen);
  }, [chosen]);
  // A window widened past the docking line has no drawer to keep open. Done
  // while rendering, the moment the line is crossed, rather than in an effect
  // after the paint: React's own pattern for state that follows a changed input.
  const [dockedBefore, setDockedBefore] = useState(docked);
  if (docked !== dockedBefore) {
    setDockedBefore(docked);
    if (docked) setDrawerOpen(false);
  }
  // #endregion docking
  return {
    docked,
    collapsed: railCollapsed,
    toggleCollapsed: () => setChosen(!railCollapsed),
    drawerOpen,
    openDrawer: () => setDrawerOpen(true),
    closeDrawer: () => setDrawerOpen(false),
  };
}
