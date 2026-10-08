/**
 * Does:      Keeps the vehicle list: asks the API for it only when a view needs it, filters, sorts and pages it,
 *            retries the first load, and lays this visitor's bids over it.
 * Does not:  Write the address bar, open a view, or draw anything.
 * Used by:   App.tsx, InventoryView.tsx, useNavigation.ts, useOpenVehicle.ts.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  fetchFacets,
  fetchVehicles,
  peekVehicles,
  type InventoryFacets,
  type VehiclePage,
} from '../../lib/data';
import type { Vehicle } from '../../lib/types';
import { byAuctionUrgency } from '../../lib/auction';
import { applyBidRecord, useBids } from '../../hooks/useBids';
import { useFirstLoad } from '../../hooks/useFirstLoad';
import type { Address } from './useAddressBar';
import { useListingRefresh } from './useListingRefresh';

export type LoadState = 'loading' | 'ready' | 'error';

/**
 * The time the browser first received each page of results. The grid is
 * ranked on this time, not the ticking clock, so a bid layered on later does
 * not re-rank the page (ADR-056). A WeakMap lets old pages be garbage collected.
 */
const answeredAt = new WeakMap<readonly Vehicle[], number>();
function timeOfAnswer(vehicles: readonly Vehicle[], answered = Date.now()): number {
  let at = answeredAt.get(vehicles);
  if (at === undefined) {
    at = answered;
    answeredAt.set(vehicles, at);
  }
  return at;
}

/** How long to let the user keep typing/clicking before asking the API to filter. */
const FILTER_DEBOUNCE_MS = 500;

/** `npm start` opens the browser before the API finishes booting, so keep
 *  retrying the first load quietly for a while before declaring an error. */
const INITIAL_RETRY_MS = 2_000;
const MAX_INITIAL_RETRIES = 15;

const EMPTY_FACETS: InventoryFacets = {
  makes: [],
  body_styles: [],
  title_statuses: [],
  provinces: [],
};
const EMPTY_PAGE: VehiclePage = { total: 0, vehicles: [] };

export type Inventory = ReturnType<typeof useInventory>;

export function useInventory(address: Address, accountEmail: string | null) {
  const { filters, sort, inventoryOpen, selectedVehicle } = address;
  // #region first-listing
  // A page the rendering service drew arrives with its first listing read (ADR: A rendering service
  // beside the API): the list starts from it, ranked at the service's clock, so this first draw is
  // the service's draw, and the fetch below skips its first run, whose answer is already on the page.
  const firstLoad = useFirstLoad();
  const listing = firstLoad?.listing ?? null;
  const [answered] = useState(() => {
    if (listing) timeOfAnswer(listing.page.vehicles, firstLoad?.nowMs);
    return listing !== null;
  });
  const skipFirstFetch = useRef(answered);
  // #endregion first-listing
  /** The server-filtered, server-sorted page currently on display. */
  const [page, setPage] = useState<VehiclePage>(listing?.page ?? EMPTY_PAGE);
  const [facets, setFacets] = useState<InventoryFacets>(listing?.facets ?? EMPTY_FACETS);
  const [loadState, setLoadState] = useState<LoadState>(answered ? 'ready' : 'loading');
  /** A filter request failed, so the list shows the previous results. */
  const [staleResults, setStaleResults] = useState(false);
  const [reloadNonce, setReloadNonce] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  // #region listing-when-shown
  // The vehicle list belongs to the inventory, not the landing page. It is a
  // slow request, and the landing page shows no vehicle. So it is fetched only
  // when a view that needs it opens: the list, a vehicle, or a ?vehicle= link.
  const needsListing = inventoryOpen || selectedVehicle !== null || address.startedOnVehicleLink;
  // #endregion listing-when-shown

  // Bid state lives in the API; refetch the list whenever it changes.
  const refreshList = useCallback(() => setReloadNonce((n) => n + 1), []);
  // Keyed on the signed-in email: bids belong to an account, so signing in or
  // out fetches that account's bids instead of showing the old badges.
  const { bids, placeBid, buyNow, resetBids } = useBids(refreshList, accountEmail);

  // #region visible-order
  /**
   * The page on display, with the buyer's bids layered on for instant feedback.
   * Under the ending-soonest sort it is also re-ranked here, in the browser.
   *
   * Why: with this many auctions the soonest one is always about to end, so
   * any page the server sends is already slightly out of date as it paints.
   * The browser applies the server's own ranking again, which moves an ended
   * auction to where the server would have put it. Which vehicles are on the
   * page, the count and the paging stay the server's. Other sorts are left
   * exactly as they arrived.
   *
   * It ranks on the time the page arrived (timeOfAnswer), not the ticking
   * clock, because re-ranking every second made cards jump. A card that ends
   * says "Ended" where it stands until the next answer lands (ADR-056).
   */
  const visibleVehicles = useMemo(() => {
    const withBids = page.vehicles.map((vehicle) => applyBidRecord(vehicle, bids[vehicle.id]));
    return sort === 'ending-soonest'
      ? byAuctionUrgency(withBids, timeOfAnswer(page.vehicles))
      : withBids;
  }, [page.vehicles, bids, sort]);
  // #endregion visible-order

  // #region facets-once
  // The filter dropdowns' options (facets) come from the API, because the page
  // only holds a slice of the data. If the request fails, the dropdowns are
  // empty rather than broken.
  //
  // Asked until answered, then never again: the server builds the options once
  // and they cannot change while the page is open (ADR-025). reloadNonce stays
  // in the dependencies so a failed first try (the API still booting under
  // `npm start`, say) is retried along with the listing.
  useEffect(() => {
    // The dropdowns belong to the inventory too, so the landing page skips them.
    if (!needsListing || facets !== EMPTY_FACETS) return;
    const controller = new AbortController();
    fetchFacets(controller.signal)
      .then(setFacets)
      .catch(() => {});
    return () => controller.abort();
  }, [reloadNonce, facets, needsListing]);
  // #endregion facets-once

  // Filtering, sorting, and paging happen on the server: every change becomes a
  // GET request. Requests are debounced (held until the user pauses for
  // FILTER_DEBOUNCE_MS) so typing doesn't spam the API, and cached per query
  // string in data.ts. A cache hit skips the debounce, since no request is
  // made. A reloadNonce bump is a refresh (a retry button, a timer): it runs
  // at once and skips the cache.
  const lastNonce = useRef(reloadNonce);
  const initialAttempts = useRef(0);
  useEffect(() => {
    if (!needsListing) return;
    if (skipFirstFetch.current) {
      skipFirstFetch.current = false;
      return;
    }
    const controller = new AbortController();
    let retryTimer: number | undefined;
    const isRefresh = reloadNonce !== lastNonce.current;
    lastNonce.current = reloadNonce;
    const firstLoad = loadState !== 'ready';
    if (firstLoad) setLoadState('loading');

    if (!firstLoad && !isRefresh) {
      const cached = peekVehicles(filters, sort);
      if (cached) {
        setPage(cached);
        setStaleResults(false);
        return;
      }
    }

    const timer = window.setTimeout(
      () => {
        fetchVehicles(filters, { sort, signal: controller.signal, forceRefresh: isRefresh })
          .then((data) => {
            initialAttempts.current = 0;
            setPage(data);
            setStaleResults(false);
            if (firstLoad) setLoadState('ready');
          })
          .catch(() => {
            if (controller.signal.aborted) return;
            if (firstLoad) {
              // The API may still be booting (npm start opens the browser
              // first), so keep retrying quietly before showing the error.
              if (initialAttempts.current < MAX_INITIAL_RETRIES) {
                initialAttempts.current += 1;
                retryTimer = window.setTimeout(
                  () => setReloadNonce((n) => n + 1),
                  INITIAL_RETRY_MS
                );
                return;
              }
              setLoadState('error');
            } else {
              setStaleResults(true);
            }
          });
      },
      firstLoad || isRefresh ? 0 : FILTER_DEBOUNCE_MS
    );
    return () => {
      window.clearTimeout(timer);
      window.clearTimeout(retryTimer);
      controller.abort();
    };
    // loadState is read above but deliberately left out of the dependencies.
    // This effect sets it to 'ready' itself, so listing it would rerun the
    // effect the moment its own load finished.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters, sort, reloadNonce, needsListing]);

  // Re-asked when its answer can change, never while nobody can see it (useListingRefresh.ts).
  useListingRefresh(page, filters.status, address.listShowing, refreshList);

  // The bid map split three ways for the badges: leading, outbid, bought outright.
  const highBidderIds = useMemo(
    () =>
      new Set(
        Object.entries(bids)
          .filter(([, bid]) => !bid.outbid)
          .map(([id]) => id)
      ),
    [bids]
  );
  const outbidIds = useMemo(
    () =>
      new Set(
        Object.entries(bids)
          .filter(([, bid]) => bid.outbid)
          .map(([id]) => id)
      ),
    [bids]
  );
  const wonIds = useMemo(
    () =>
      new Set(
        Object.entries(bids)
          .filter(([, b]) => b.won_buy_now)
          .map(([id]) => id)
      ),
    [bids]
  );

  /**
   * Appends the next page from the server, skipping any vehicle already shown.
   * A filter or sort change starts over through the fetch effect instead.
   */
  const loadMore = async () => {
    if (loadingMore) return;
    setLoadingMore(true);
    try {
      const next = await fetchVehicles(filters, { sort, offset: page.vehicles.length });
      setPage((prev) => {
        const seen = new Set(prev.vehicles.map((v) => v.id));
        return {
          total: next.total,
          vehicles: [...prev.vehicles, ...next.vehicles.filter((v) => !seen.has(v.id))],
        };
      });
    } catch {
      setStaleResults(true);
    } finally {
      setLoadingMore(false);
    }
  };

  const bidCount = Object.keys(bids).length;
  /** Asks first, because a cleared bid cannot come back. */
  const confirmResetBids = () => {
    if (
      window.confirm(
        `Clear your ${bidCount === 1 ? 'bid' : `${bidCount} bids`}? This can't be undone.`
      )
    ) {
      void resetBids();
    }
  };

  return {
    page,
    facets,
    loadState,
    staleResults,
    loadingMore,
    visibleVehicles,
    highBidderIds,
    outbidIds,
    wonIds,
    bids,
    bidCount,
    placeBid,
    buyNow,
    confirmResetBids,
    loadMore,
    /** Ask again at once, skipping the cache: the stale banner's Retry. */
    refresh: refreshList,
    /** The error notice's Try again: the quiet first-load retries start over too. */
    retryFirstLoad: () => {
      initialAttempts.current = 0;
      setReloadNonce((n) => n + 1);
    },
  };
}
