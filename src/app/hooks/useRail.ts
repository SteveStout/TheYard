/**
 * Does:      Says how the sidebar sits: docked open, docked collapsed, or a drawer on a narrow screen, and remembers a collapsed rail between visits.
 * Does not:  Draw the sidebar (SideNav does) or choose what is in it.
 * Used by:   App.tsx, Shell.tsx.
 */
import { useEffect, useState } from 'react';
import { readRailCollapsed, storeRailCollapsed } from '../../components/layout/SideNav';
import { useMediaQuery } from '../../hooks/useMediaQuery';
import { DESK } from '../../lib/breakpoints';

export type Rail = ReturnType<typeof useRail>;

export function useRail() {
  // #region docking
  /** The sidebar (ADR-013): a docked rail at 1024px and up, a drawer below. */
  const docked = useMediaQuery(DESK);
  const [railCollapsed, setRailCollapsed] = useState(readRailCollapsed);
  const [drawerOpen, setDrawerOpen] = useState(false);
  // Remember a collapsed rail between visits.
  useEffect(() => {
    storeRailCollapsed(railCollapsed);
  }, [railCollapsed]);
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
    toggleCollapsed: () => setRailCollapsed((collapsed) => !collapsed),
    drawerOpen,
    openDrawer: () => setDrawerOpen(true),
    closeDrawer: () => setDrawerOpen(false),
  };
}
