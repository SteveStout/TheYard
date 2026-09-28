/**
 * Does:      Re-asks the vehicle list at the next moment its answer can change, and never while nobody can see it.
 * Does not:  Fetch the list itself (useInventory.ts does, when asked), or decide what is on it.
 * Used by:   useInventory.ts.
 */
import { useEffect, useRef } from 'react';
import type { VehiclePage } from '../../lib/data';
import { nextAuctionBoundary } from '../../lib/auction';
import type { InventoryFilters } from '../../lib/inventory';

/**
 * With a status filter on, the list drifts as auctions elsewhere end, even when
 * nothing on this page is about to start or end. Refresh it this often.
 */
const STATUS_REFRESH_MS = 60_000;

/**
 * The most often a listing re-asks the API, however fast its auctions end.
 * Asking more often only moves cards under the reader's eye (ADR-056).
 */
const LISTING_REFRESH_FLOOR_MS = 60_000;

/** Ask a moment after an auction starts or ends, so the server has seen it too. */
const BOUNDARY_GRACE_MS = 750;

/**
 * page is the list on screen, status the status filter (a filter on it keeps
 * the list drifting), listShowing whether a reader can see the list, and
 * refresh what asks the API again. refresh is the same function on every
 * render, so naming it in the effects below never re-runs them.
 */
export function useListingRefresh(
  page: VehiclePage,
  status: InventoryFilters['status'],
  listShowing: boolean,
  refresh: () => void
) {
  // #region listing-goes-stale
  // A listing stays on screen for minutes, and auctions start and end while it
  // does (ADR-056). Under the default ending-soonest sort, ended lots would sit
  // at the top of the front page.
  //
  // So the list is re-asked at the next moment its answer can change: the
  // soonest auction start or end still ahead, but never more often than
  // LISTING_REFRESH_FLOOR_MS. With nothing left to cross and a status filter
  // on, the slower STATUS_REFRESH_MS timer applies.
  //
  // No refresh while the tab is hidden (it would drain battery for nobody), or
  // while another view covers the list. missedRefresh remembers the skipped
  // refresh, and it runs when the list is visible again.
  const missedRefresh = useRef(false);
  useEffect(() => {
    if (listShowing && missedRefresh.current) {
      missedRefresh.current = false;
      refresh();
    }
  }, [listShowing, refresh]);
  useEffect(() => {
    const onVisible = () => {
      if (!document.hidden && missedRefresh.current) {
        missedRefresh.current = false;
        refresh();
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, [refresh]);

  // Schedule the next refresh from the soonest start or end on this page.
  useEffect(() => {
    const boundary = nextAuctionBoundary(page.vehicles, Date.now());
    const delay =
      boundary === null
        ? status
          ? STATUS_REFRESH_MS
          : null
        : Math.max(LISTING_REFRESH_FLOOR_MS, boundary - Date.now() + BOUNDARY_GRACE_MS);
    if (delay === null) return;

    const id = window.setTimeout(() => {
      if (document.hidden || !listShowing) {
        missedRefresh.current = true;
        return;
      }
      refresh();
    }, delay);
    return () => window.clearTimeout(id);
  }, [page, status, listShowing, refresh]);
  // #endregion listing-goes-stale
}
