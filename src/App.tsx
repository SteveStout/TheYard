// App.tsx is the shell of the site. It decides which view is showing (home,
// inventory, a vehicle, Admin, Account, or a decision record), keeps that view
// in the address bar so every view is a shareable link, and loads the
// inventory from the API. Everything it draws is a component; this file wires
// state and handlers to them.
//
// Regions, in order: admin-on-demand, admin-card, listing-when-shown, docking,
// visible-order, facets-once, who, url-mirror, back-forward, listing-goes-stale,
// refresh-open-vehicle, focus, history, open-inventory, open-document,
// open-account, announcement. In the JSX: skip-link, header-below-dock,
// store-bar, footer-version.
import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  fetchFacets,
  fetchVehicleById,
  fetchVehicles,
  peekVehicles,
  type InventoryFacets,
  type VehiclePage,
} from './lib/data';
import type { Vehicle } from './lib/types';
import { byAuctionUrgency, nextAuctionBoundary } from './lib/auction';
import { ICON } from './lib/icons';
import {
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  opensInventory,
  type InventoryFilters,
  type SortKey,
} from './lib/inventory';
import { applyBidRecord, useBids } from './hooks/useBids';
import { cardFromAddress, pinFromAddress, type CardSlug } from './lib/workbench';
import { useNow } from './hooks/useNow';
import { prefetchWhenIdle } from './lib/prefetch';
// #region admin-on-demand
// The Admin tab is the biggest view in the app, and few visitors open it.
// lazy() splits it into its own file that the browser downloads only when
// ?view=admin opens, so the landing page does not pay for it.
const loadAdminPanel = () => import('./components/admin/AdminPanel/AdminPanel');
const AdminPanel = lazy(() =>
  loadAdminPanel().then((module) => ({
    default: module.AdminPanel,
  }))
);
// #endregion admin-on-demand
import { AccountPanel } from './components/account/AccountPanel';
import { accountQuestion, SIGNED_OUT, type Account } from './lib/auth';
import { readRailCollapsed, SideNav, storeRailCollapsed } from './components/layout/SideNav';
import { docKeyForSlug, docSlug, LINKS, type DocKey } from './components/docs/DocsMenu';
import { BrandMark } from './components/layout/BrandMark';
import { StoreBar } from './components/layout/StoreBar';
import { Background } from './components/layout/Background';
import { Landing } from './components/landing/Landing';
import { NavGlyph } from './components/shared/SheetIcons';
import { useMediaQuery } from './hooks/useMediaQuery';
import { DESK } from './lib/breakpoints';
import { FilterBar } from './components/inventory/FilterBar';
import { InventoryGrid } from './components/inventory/InventoryGrid';
import { VehicleDetail } from './components/vehicle/VehicleDetail';
import styles from './App.module.css';

type LoadState = 'loading' | 'ready' | 'error';

/**
 * The time the browser first received each page of results. The grid is
 * ranked on this time, not the ticking clock, so a bid layered on later does
 * not re-rank the page (ADR-056). A WeakMap lets old pages be garbage collected.
 */
const answeredAt = new WeakMap<readonly Vehicle[], number>();
function timeOfAnswer(vehicles: readonly Vehicle[]): number {
  let at = answeredAt.get(vehicles);
  if (at === undefined) {
    at = Date.now();
    answeredAt.set(vehicles, at);
  }
  return at;
}

/** How long to let the user keep typing/clicking before asking the API to filter. */
const FILTER_DEBOUNCE_MS = 500;

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

/** The address, read once at startup. Filters live in it (?make=Ford&status=live). */
const INITIAL_PARAMS = new URLSearchParams(window.location.search);
const INITIAL_URL_STATE = filtersFromSearchParams(INITIAL_PARAMS);
/** A tile click is GET navigation: ?vehicle={id} deep-links the detail view. */
const INITIAL_VEHICLE_ID = INITIAL_PARAMS.get('vehicle');
// A password reset link's token, read once: the address bar loses it on the first render.
const INITIAL_RESET = INITIAL_PARAMS.get('reset');
/** ?doc=adr-lockout opens that record. A name that matches no record opens nothing. */
const INITIAL_DOC = docKeyForSlug(INITIAL_PARAMS.get('doc'));
/**
 * The landing page is home. The inventory is ?view=inventory, and an address
 * with a filter, a sort or ?vehicle= opens the inventory too.
 */
const INITIAL_INVENTORY = opensInventory(INITIAL_PARAMS);
/** The Admin card an address names (?view=admin&card=timing&pin=errors), resolved once. */
const INITIAL_CARD = cardFromAddress(INITIAL_PARAMS.get('card'));

/** Where a view's back button goes. Its label says which: "Back to home" or inventory. */
export type BackTo = 'home' | 'inventory';

export default function App() {
  /** The server-filtered, server-sorted page currently on display. */
  const [page, setPage] = useState<VehiclePage>(EMPTY_PAGE);
  const [facets, setFacets] = useState<InventoryFacets>(EMPTY_FACETS);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  /** A filter request failed, so the list shows the previous results. */
  const [staleResults, setStaleResults] = useState(false);
  const [reloadNonce, setReloadNonce] = useState(0);
  /** Snapshot of the opened vehicle, so the detail view survives page refetches. */
  const [selectedVehicle, setSelectedVehicle] = useState<Vehicle | null>(null);
  const [filters, setFilters] = useState<InventoryFilters>(INITIAL_URL_STATE.filters);
  const [sort, setSort] = useState<SortKey>(INITIAL_URL_STATE.sort);
  const [loadingMore, setLoadingMore] = useState(false);
  /** The Admin tab (ADR-010): health, errors, and Azure's view, ?view=admin. */
  const [adminOpen, setAdminOpen] = useState(INITIAL_PARAMS.get('view') === 'admin');
  // #region admin-card
  // The Admin workbench's open card and its pinned card (ADR-080). They live
  // here, beside the view, because the url-mirror effect below is the only code
  // that writes the address bar. If the address names a card that does not
  // exist, cardAsked keeps the name so the rail can say so, and Health opens.
  const [adminCard, setAdminCard] = useState<CardSlug>(INITIAL_CARD.slug);
  const [adminPin, setAdminPin] = useState<CardSlug | null>(
    pinFromAddress(INITIAL_PARAMS.get('pin'))
  );
  const [cardAsked, setCardAsked] = useState<string | null>(
    INITIAL_CARD.known ? null : INITIAL_CARD.asked
  );
  // #endregion admin-card
  /** The account view (ADR-037), ?view=account. */
  const [accountOpen, setAccountOpen] = useState(INITIAL_PARAMS.get('view') === 'account');
  /** True when the base view is the inventory list rather than the landing page. */
  const [inventoryOpen, setInventoryOpen] = useState(INITIAL_INVENTORY);
  const [openDocKey, setOpenDocKey] = useState<DocKey | null>(INITIAL_DOC);
  // #region listing-when-shown
  // The vehicle list belongs to the inventory, not the landing page. It is a
  // slow request, and the landing page shows no vehicle. So it is fetched only
  // when a view that needs it opens: the list, a vehicle, or a ?vehicle= link.
  const needsListing = inventoryOpen || selectedVehicle !== null || INITIAL_VEHICLE_ID !== null;
  // #endregion listing-when-shown

  const [account, setAccount] = useState<Account>(SIGNED_OUT);
  const now = useNow();
  /** The running build, reported by the container itself (ADR-005). */
  const [build, setBuild] = useState<{ version: string; commit: string } | null>(null);
  // #region docking
  /** The sidebar (ADR-013): a docked rail at 1024px and up, a drawer below. */
  const docked = useMediaQuery(DESK);
  const [railCollapsed, setRailCollapsed] = useState(readRailCollapsed);
  const [drawerOpen, setDrawerOpen] = useState(false);
  // Remember a collapsed rail between visits.
  useEffect(() => {
    storeRailCollapsed(railCollapsed);
  }, [railCollapsed]);
  // A window widened past the docking line has no drawer to keep open.
  useEffect(() => {
    if (docked) setDrawerOpen(false);
  }, [docked]);
  // #endregion docking
  // #region fetch-ahead
  // The two chunks a click most often waits on, fetched once the first page has
  // loaded and the browser is idle: the renderer every document needs and the
  // Admin tab. Nothing here is on the first page's bytes, and a reader who asked
  // to save data, or is on 2G, gets none of it (src/lib/prefetch.ts).
  useEffect(() => prefetchWhenIdle([() => import('./lib/markdown'), loadAdminPanel]), []);
  // #endregion fetch-ahead
  // Bid state lives in the API; refetch the list whenever it changes.
  const refreshList = useCallback(() => setReloadNonce((n) => n + 1), []);
  // Keyed on the signed-in email: bids belong to an account, so signing in or
  // out fetches that account's bids instead of showing the old badges.
  const { bids, placeBid, buyNow, resetBids } = useBids(refreshList, account.email);

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

  // The footer's version line: ask the running API which build it is (ADR-005).
  useEffect(() => {
    fetch('/api/version')
      .then((r) => (r.ok ? r.json() : null))
      .then(setBuild)
      .catch(() => {});
  }, []);

  // #region who
  // Who is signed in, if anyone. The session is an httpOnly cookie (one that
  // page scripts cannot read), so the page has to ask the API (ADR-037). A
  // failure leaves the visitor signed out, which is the safe answer.
  //
  // The answer is dropped if the account changed while the question was out.
  // Otherwise a visitor who registers quickly could be signed out again by a
  // late "nobody" answer. Every change the page makes goes through
  // changeAccount, which tells the question.
  const [whoIsSignedIn] = useState(accountQuestion);
  const changeAccount = useCallback(
    (next: Account) => {
      whoIsSignedIn.changed();
      setAccount(next);
    },
    [whoIsSignedIn]
  );
  useEffect(() => {
    whoIsSignedIn.ask(setAccount).catch(() => {});
  }, [whoIsSignedIn]);
  // #endregion who

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

  // #region url-mirror
  // Mirror the current view into the address bar: the filter GET parameters
  // plus ?vehicle={id} when a detail page is open. replaceState here, because
  // typing shouldn't pile up history entries; opening a tile pushes its own
  // entry (below) so the browser's Back button closes the detail view.
  const deepLinkPending = useRef(INITIAL_VEHICLE_ID !== null);
  useEffect(() => {
    if (deepLinkPending.current) return;
    // The landing page carries no filters: they belong to the inventory, and an
    // address with one in it opens the inventory (opensInventory).
    const onLanding = !inventoryOpen && !selectedVehicle && !adminOpen && !accountOpen;
    const params = onLanding ? new URLSearchParams() : filtersToSearchParams(filters, sort);
    if (selectedVehicle) params.set('vehicle', selectedVehicle.id);
    const listOnly = inventoryOpen && !selectedVehicle && !adminOpen && !accountOpen;
    if (listOnly && !opensInventory(params)) params.set('view', 'inventory');
    if (adminOpen) {
      params.set('view', 'admin');
      // Health is the card ?view=admin opens, so it is the one card the address leaves unnamed.
      if (adminCard !== 'health') params.set('card', adminCard);
      if (adminPin !== null) params.set('pin', adminPin);
    }
    if (accountOpen) params.set('view', 'account');
    if (openDocKey) params.set('doc', docSlug(openDocKey));
    const query = params.toString();
    window.history.replaceState(
      window.history.state,
      '',
      query ? `?${query}` : window.location.pathname
    );
  }, [
    filters,
    sort,
    selectedVehicle,
    adminOpen,
    adminCard,
    adminPin,
    accountOpen,
    openDocKey,
    inventoryOpen,
  ]);
  // #endregion url-mirror

  // Restore a deep-linked detail view on first load (?vehicle={id}).
  useEffect(() => {
    if (!INITIAL_VEHICLE_ID) return;
    let live = true;
    fetchVehicleById(INITIAL_VEHICLE_ID)
      .then((vehicle) => {
        if (!live) return;
        deepLinkPending.current = false;
        if (vehicle) {
          setSelectedVehicle(vehicle);
        } else {
          // Unknown id: drop the dead parameter and stay on the list.
          const params = new URLSearchParams(window.location.search);
          params.delete('vehicle');
          const query = params.toString();
          window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
        }
      })
      .catch(() => {
        deepLinkPending.current = false;
      });
    return () => {
      live = false;
    };
  }, []);

  // #region back-forward
  // Browser Back/Forward: re-read the whole view from the URL. The listener is
  // registered once, so it reads the open vehicle through selectedIdRef.
  const selectedIdRef = useRef<string | null>(null);
  useEffect(() => {
    selectedIdRef.current = selectedVehicle?.id ?? null;
  }, [selectedVehicle]);
  useEffect(() => {
    const onPopState = () => {
      const params = new URLSearchParams(window.location.search);
      const restored = filtersFromSearchParams(params);
      setFilters(restored.filters);
      setSort(restored.sort);
      setAdminOpen(params.get('view') === 'admin');
      const card = cardFromAddress(params.get('card'));
      setAdminCard(card.slug);
      setAdminPin(pinFromAddress(params.get('pin')));
      setCardAsked(card.known ? null : card.asked);
      setAccountOpen(params.get('view') === 'account');
      setInventoryOpen(opensInventory(params));
      setOpenDocKey(docKeyForSlug(params.get('doc')));
      const vehicleId = params.get('vehicle');
      if (!vehicleId) {
        setSelectedVehicle(null);
        return;
      }
      if (vehicleId === selectedIdRef.current) return;
      fetchVehicleById(vehicleId)
        .then((vehicle) => setSelectedVehicle(vehicle))
        .catch(() => setSelectedVehicle(null));
    };
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);
  // #endregion back-forward

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
  const listShowing =
    inventoryOpen && !adminOpen && !accountOpen && !selectedVehicle && !openDocKey;
  useEffect(() => {
    if (listShowing && missedRefresh.current) {
      missedRefresh.current = false;
      setReloadNonce((n) => n + 1);
    }
  }, [listShowing]);
  useEffect(() => {
    const onVisible = () => {
      if (!document.hidden && missedRefresh.current) {
        missedRefresh.current = false;
        setReloadNonce((n) => n + 1);
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, []);

  // Schedule the next refresh from the soonest start or end on this page.
  useEffect(() => {
    const boundary = nextAuctionBoundary(page.vehicles, Date.now());
    const delay =
      boundary === null
        ? filters.status
          ? STATUS_REFRESH_MS
          : null
        : Math.max(LISTING_REFRESH_FLOOR_MS, boundary - Date.now() + BOUNDARY_GRACE_MS);
    if (delay === null) return;

    const id = window.setTimeout(() => {
      if (document.hidden || !listShowing) {
        missedRefresh.current = true;
        return;
      }
      setReloadNonce((n) => n + 1);
    }, delay);
    return () => window.clearTimeout(id);
  }, [page, filters.status, listShowing]);
  // #endregion listing-goes-stale

  // Placing a bid does not mean you are leading: another bidder may have
  // answered (ADR-027). The server decides; the page only reads its answer.
  // #region refresh-open-vehicle
  // The page can overlay current_bid, but not min_next_bid: the bid increment
  // rules live only on the server. So when another bidder moves the price on
  // the open vehicle, it is refetched rather than patched. Otherwise the panel
  // could show a minimum bid that the server would reject.
  const openMarketAmount = selectedVehicle
    ? (bids[selectedVehicle.id]?.market_amount ?? null)
    : null;
  useEffect(() => {
    const id = selectedIdRef.current;
    if (!id || openMarketAmount === null) return;
    let cancelled = false;
    void fetchVehicleById(id)
      .then((fresh) => {
        if (!cancelled && fresh && selectedIdRef.current === fresh.id) setSelectedVehicle(fresh);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [openMarketAmount]);
  // #endregion refresh-open-vehicle

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

  /** The snapshot with the buyer's live bid state layered on. */
  const selected = useMemo(
    () => (selectedVehicle ? applyBidRecord(selectedVehicle, bids[selectedVehicle.id]) : undefined),
    [selectedVehicle, bids]
  );

  // #region focus
  // A single-page app swaps the view without changing the document, so the
  // browser has nothing to move focus to. Open a vehicle from a tile and the
  // tile is gone: focus falls to <body>, the next Tab starts from the top of
  // the page, and a screen reader announces nothing at all. Moving focus to
  // the region that just changed is the standard repair, and it is why <main>
  // carries tabIndex={-1}: focusable by script, never by Tab.
  const mainRef = useRef<HTMLElement>(null);
  const viewKey = adminOpen
    ? 'admin'
    : accountOpen
      ? 'account'
      : selectedVehicle
        ? `vehicle:${selectedVehicle.id}`
        : 'list';
  const lastView = useRef(viewKey);
  useEffect(() => {
    if (lastView.current === viewKey) return;
    const arrivedByItself = deepLinkPending.current;
    lastView.current = viewKey;
    // A deep-linked vehicle resolving is data arriving, not a view the visitor
    // chose. Moving focus for it yanks a screen reader's cursor mid-sentence
    // seconds into the page, and pulls a keyboard user off the skip link.
    if (arrivedByItself) return;
    // preventScroll because the effect below already decides where the page
    // sits; without it the two fight and the detail opens part-scrolled.
    mainRef.current?.focus({ preventScroll: true });
  }, [viewKey]);
  // #endregion focus

  // #region history
  // Open the detail at the top; restore the list scroll position on back.
  const listScrollY = useRef(0);
  const openVehicle = (vehicle: Vehicle) => {
    listScrollY.current = window.scrollY;
    // Real GET navigation: push a history entry so the browser's Back works.
    const params = filtersToSearchParams(filters, sort);
    params.set('vehicle', vehicle.id);
    window.history.pushState({ viaTile: true }, '', `?${params}`);
    setInventoryOpen(true);
    setSelectedVehicle(vehicle);
  };
  /** From the account page's bid list: open that vehicle's detail view. */
  const openVehicleById = (vehicleId: string) => {
    void fetchVehicleById(vehicleId).then((vehicle) => {
      if (!vehicle) return;
      setAccountOpen(false);
      openVehicle(vehicle);
    });
  };
  /** What the entry that opened this view recorded, read when the view draws its back button. */
  const backTo = (): BackTo =>
    (window.history.state as { from?: BackTo } | null)?.from === 'home' ? 'home' : 'inventory';
  const backToInventory = () => {
    // If we pushed this entry, going back keeps history clean; a deep-linked
    // visit has no list entry behind it, so just swap the URL in place.
    if ((window.history.state as { viaTile?: boolean } | null)?.viaTile) {
      window.history.back();
      return;
    }
    setSelectedVehicle(null);
    setInventoryOpen(true);
    const query = filtersToSearchParams(filters, sort).toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };
  // #endregion history

  useEffect(() => {
    // Every view except the list opens at the top; the list returns to where
    // the reader left it. Admin counts as a view switch too.
    window.scrollTo(
      0,
      selectedVehicle || adminOpen || accountOpen || !inventoryOpen ? 0 : listScrollY.current
    );
  }, [selectedVehicle, adminOpen, accountOpen, inventoryOpen]);

  // #region open-inventory
  // The same shape as Admin and Account: a view at its own address, pushed so
  // Back returns to the landing page it was opened from.
  const openInventory = () => {
    const params = filtersToSearchParams(filters, sort);
    if (!opensInventory(params)) params.set('view', 'inventory');
    window.history.pushState({ viaInventory: true }, '', `?${params}`);
    setSelectedVehicle(null);
    setAdminOpen(false);
    setAccountOpen(false);
    setInventoryOpen(true);
  };
  // #endregion open-inventory

  // Where Back goes is stored in the history entry. Opened from the landing
  // page, the button says "Back to home"; opened from the inventory, or
  // straight from its address, it says inventory.
  const openedFrom = (): BackTo => (inventoryOpen ? 'inventory' : 'home');
  const openAdmin = () => {
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'admin');
    window.history.pushState({ viaAdmin: true, from: openedFrom() }, '', `?${params}`);
    setSelectedVehicle(null);
    setAccountOpen(false);
    setAdminCard('health');
    setAdminPin(null);
    setCardAsked(null);
    setAdminOpen(true);
  };
  /**
   * A card on the workbench is a place, so opening one pushes an entry and Back
   * returns to the card before it. The entry records benchDepth (how many cards
   * deep it is), so the view's own back button leaves the Admin tab in one step
   * however many cards were opened.
   */
  const openAdminCard = (slug: CardSlug) => {
    if (slug === adminCard && cardAsked === null) return;
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'admin');
    if (slug !== 'health') params.set('card', slug);
    if (adminPin !== null) params.set('pin', adminPin);
    const held = (window.history.state ?? {}) as {
      viaAdmin?: boolean;
      from?: BackTo;
      benchDepth?: number;
    };
    window.history.pushState(
      { viaAdmin: held.viaAdmin === true, from: held.from, benchDepth: (held.benchDepth ?? 0) + 1 },
      '',
      `?${params}`
    );
    setAdminCard(slug);
    setCardAsked(null);
  };

  // #region open-document
  // A decision record is a view with its own address (?doc=...), the same shape
  // as Admin and Account, so a record can be sent to someone as a link (ADR-057).
  const openDocument = (key: DocKey | null) => {
    if (key !== null) {
      const params = filtersToSearchParams(filters, sort);
      params.set('doc', docSlug(key));
      // One history entry for "a record is open", however many records are
      // opened while it is, so Escape closes the dialog instead of stepping
      // back through each record. The dialog is modal, so today the replace
      // branch is unreachable; it guards a future non-modal dialog.
      const url = `?${params}`;
      if (openDocKey === null) {
        window.history.pushState({ viaDoc: true }, '', url);
      } else {
        window.history.replaceState({ viaDoc: true }, '', url);
      }
      setOpenDocKey(key);
      return;
    }
    // Closed by Escape, the X, or the backdrop. If we pushed the entry that
    // opened it, going back keeps history clean and makes Back and Escape
    // agree; a visitor who arrived on the link has no entry behind it, so the
    // parameter is swapped out in place instead of leaving the site.
    if ((window.history.state as { viaDoc?: boolean } | null)?.viaDoc) {
      window.history.back();
      return;
    }
    setOpenDocKey(null);
  };
  // #endregion open-document

  // #region open-account
  // The same shape as Admin, because it is the same kind of thing: one more
  // view at its own ?view= value, pushed so Back closes it.
  const openAccount = () => {
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'account');
    window.history.pushState({ viaAccount: true, from: openedFrom() }, '', `?${params}`);
    setSelectedVehicle(null);
    setAdminOpen(false);
    setAccountOpen(true);
  };
  const closeAccount = () => {
    if ((window.history.state as { viaAccount?: boolean } | null)?.viaAccount) {
      window.history.back();
      return;
    }
    setAccountOpen(false);
    setInventoryOpen(true);
    const query = filtersToSearchParams(filters, sort).toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };
  // #endregion open-account
  // Leave Admin in one step, going back past every workbench card opened.
  const closeAdmin = () => {
    const held = window.history.state as { viaAdmin?: boolean; benchDepth?: number } | null;
    if (held?.viaAdmin) {
      window.history.go(-((held.benchDepth ?? 0) + 1));
      return;
    }
    setAdminOpen(false);
    setInventoryOpen(true);
    const query = filtersToSearchParams(filters, sort).toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };

  /**
   * The brand button, in the rail and the phone header, always goes to the
   * landing page. Pushed, so Back returns to the view it left.
   */
  const goHome = () => {
    window.history.pushState({ viaHome: true }, '', window.location.pathname);
    setSelectedVehicle(null);
    setAdminOpen(false);
    setAccountOpen(false);
    setInventoryOpen(false);
  };

  const patchFilters = (patch: Partial<InventoryFilters>) =>
    setFilters((prev) => ({ ...prev, ...patch }));
  const clearFilters = () => setFilters(EMPTY_FILTERS);

  const bidCount = Object.keys(bids).length;

  const handleResetBids = () => {
    if (
      window.confirm(
        `Clear your ${bidCount === 1 ? 'bid' : `${bidCount} bids`}? This can't be undone.`
      )
    ) {
      void resetBids();
    }
  };

  const handlePlaceBid = async (amount: number) => {
    if (!selected) return { kind: 'rejected' as const, reason: 'No vehicle selected.' };
    const result = await placeBid(selected, amount);
    if (result.vehicle) setSelectedVehicle(result.vehicle);
    return result.outcome;
  };

  const handleBuyNow = async () => {
    if (!selected) return { kind: 'rejected' as const, reason: 'No vehicle selected.' };
    const result = await buyNow(selected);
    if (result.vehicle) setSelectedVehicle(result.vehicle);
    return result.outcome;
  };

  // #region announcement
  // Focus says where you are; this says what you arrived at. Moving focus to a
  // container is announced inconsistently across screen readers, so the view's
  // name is stated outright. The match count is deliberately not repeated here:
  // the filter bar's own status line already owns that sentence, and two live
  // regions saying the same thing is worse than one saying it once.
  const announcement = adminOpen
    ? 'Admin panel'
    : accountOpen
      ? 'Account'
      : selectedVehicle
        ? `${selectedVehicle.year} ${selectedVehicle.make} ${selectedVehicle.model}, vehicle detail`
        : !inventoryOpen
          ? 'The Yard, home'
          : loadState === 'loading'
            ? 'Loading inventory'
            : loadState === 'error'
              ? 'The inventory API could not be reached'
              : 'Vehicle inventory';
  // #endregion announcement

  return (
    <div
      className={styles.app}
      data-rail={docked ? (railCollapsed ? 'collapsed' : 'open') : 'drawer'}
    >
      {/* #region skip-link */}
      {/* First in the tab order on purpose. The docked rail is around thirty
          buttons, and without this a keyboard user tabs every one of them to
          reach the grid, on every page view. Hidden until it has focus. */}
      <a className={styles.skipLink} href="#main-content">
        Skip to content
      </a>
      {/* #endregion skip-link */}
      {/* The one navigation surface (ADR-013): a docked rail on wide screens,
          a drawer behind the header's hamburger below 1024px. */}
      <SideNav
        docked={docked}
        collapsed={railCollapsed}
        onToggleCollapsed={() => setRailCollapsed((collapsed) => !collapsed)}
        drawerOpen={drawerOpen}
        onDrawerClose={() => setDrawerOpen(false)}
        onHome={goHome}
        homeOpen={!inventoryOpen && !adminOpen && !accountOpen && !selectedVehicle && !openDocKey}
        inventoryOpen={
          inventoryOpen && !adminOpen && !accountOpen && !selectedVehicle && !openDocKey
        }
        onOpenInventory={openInventory}
        adminOpen={adminOpen}
        onOpenAdmin={openAdmin}
        accountOpen={accountOpen}
        onOpenAccount={openAccount}
        accountEmail={account.email}
        bidCount={bidCount}
        onResetBids={handleResetBids}
        build={build}
        openDocKey={openDocKey}
        onDocChange={openDocument}
      />

      <div className={styles.page}>
        <Background />
        {/* #region header-below-dock */}
        {/* Below the docking line the header carries the brand, Reset bids,
            and the hamburger; the docked rail makes it redundant above it. */}
        {!docked && (
          <header className={styles.header} data-frame="header">
            <div className={styles.headerInner}>
              <button type="button" className={styles.brand} onClick={goHome}>
                <BrandMark size={18} className={styles.brandMark} />
                The Yard
                <span className={styles.brandSub}>Vehicle Auctions</span>
              </button>
              <div className={styles.headerActions}>
                {bidCount > 0 && (
                  <button type="button" className={styles.resetBids} onClick={handleResetBids}>
                    Reset bids ({bidCount})
                  </button>
                )}
                {/* The resume as an icon here; the docked rail has its own row for it. */}
                <a
                  className={styles.headerIcon}
                  href={LINKS.resume.href}
                  target="_blank"
                  rel="noreferrer"
                  aria-label={LINKS.resume.label}
                  title={LINKS.resume.label}
                  data-testid="header-resume"
                >
                  <NavGlyph icon="resume" size={22} />
                </a>
                <button
                  type="button"
                  className={styles.hamburger}
                  aria-label="Menu"
                  aria-haspopup="dialog"
                  aria-expanded={drawerOpen}
                  onClick={() => setDrawerOpen(true)}
                >
                  <svg viewBox="0 0 20 20" width={ICON.md} height={ICON.md} aria-hidden="true">
                    <path
                      d="M3 5h14M3 10h14M3 15h14"
                      stroke="currentColor"
                      strokeWidth={ICON.stroke}
                      strokeLinecap="round"
                    />
                  </svg>
                </button>
              </div>
            </div>
          </header>
        )}
        {/* #endregion header-below-dock */}

        {/* #region store-bar */}
        {/* The store toggle (SQL or Cosmos DB), at the top of every view on
            every width (ADR-066). Above main so it is not part of the view
            that announces itself, and below the phone header so the
            hamburger keeps its corner. */}
        <StoreBar />
        {/* #endregion store-bar */}

        <main className={styles.main} id="main-content" ref={mainRef} tabIndex={-1}>
          <p className={styles.srOnly} role="status" data-testid="view-announcement">
            {announcement}
          </p>
          {adminOpen ? (
            <Suspense fallback={<p className={styles.adminLoading}>Reading the machines...</p>}>
              <AdminPanel
                onBack={closeAdmin}
                backTo={backTo()}
                card={adminCard}
                pin={adminPin}
                cardAsked={cardAsked}
                onOpenCard={openAdminCard}
                onPin={setAdminPin}
                signedIn={account.signedIn}
                onOpenAccount={openAccount}
              />
            </Suspense>
          ) : accountOpen ? (
            <AccountPanel
              account={account}
              onAccountChange={changeAccount}
              onOpenVehicle={openVehicleById}
              onBack={closeAccount}
              backTo={backTo()}
              resetToken={INITIAL_RESET}
            />
          ) : !inventoryOpen ? (
            <Landing
              accountLabel={account.email ?? 'Sign in'}
              onOpenInventory={openInventory}
              onOpenAccount={openAccount}
              onOpenAdmin={openAdmin}
              onOpenDoc={openDocument}
            />
          ) : loadState === 'loading' ? (
            <p className={`${styles.notice} ${styles.loadingInventory}`} role="status">
              Loading inventory…
            </p>
          ) : loadState === 'error' ? (
            <div className={styles.notice} role="alert">
              <p className={styles.noticeTitle}>Couldn't reach the inventory API.</p>
              <p>
                Make sure it's running (<code>npm run api</code> in a second terminal), then try
                again.
              </p>
              <button
                type="button"
                className={styles.retry}
                onClick={() => {
                  initialAttempts.current = 0;
                  setReloadNonce((n) => n + 1);
                }}
              >
                Try again
              </button>
            </div>
          ) : selected ? (
            <VehicleDetail
              key={selected.id}
              vehicle={selected}
              now={now}
              onBack={backToInventory}
              isHighBidder={highBidderIds.has(selected.id)}
              isOutbid={outbidIds.has(selected.id)}
              wonBuyNow={wonIds.has(selected.id)}
              signedIn={account.signedIn}
              onOpenAccount={openAccount}
              onPlaceBid={handlePlaceBid}
              onBuyNow={handleBuyNow}
            />
          ) : (
            <section aria-label="Vehicle inventory">
              {/* No welcome banner here: the landing page owns that copy. */}
              <div className={styles.listHeader}>
                <h1 className={styles.listTitle}>Inventory</h1>
              </div>
              <FilterBar
                filters={filters}
                onFiltersChange={patchFilters}
                sort={sort}
                onSortChange={setSort}
                onClear={clearFilters}
                makes={facets.makes}
                bodyStyles={facets.body_styles}
                titleStatuses={facets.title_statuses}
                provinces={facets.provinces}
                shownCount={visibleVehicles.length}
                totalCount={page.total}
              />
              {staleResults && (
                <div className={styles.staleBanner} role="alert">
                  Couldn't update results from the API. Showing the previous list.
                  <button
                    type="button"
                    className={styles.staleRetry}
                    onClick={() => setReloadNonce((n) => n + 1)}
                  >
                    Retry
                  </button>
                </div>
              )}
              <InventoryGrid
                vehicles={visibleVehicles}
                now={now}
                onSelect={openVehicle}
                highBidderIds={highBidderIds}
                outbidIds={outbidIds}
                wonIds={wonIds}
                onClearFilters={clearFilters}
              />
              {page.vehicles.length < page.total && (
                <div className={styles.loadMoreRow}>
                  <button
                    type="button"
                    className={styles.loadMore}
                    onClick={() => void loadMore()}
                    disabled={loadingMore}
                  >
                    {loadingMore ? 'Loading…' : 'Load more vehicles'}
                  </button>
                </div>
              )}
            </section>
          )}
        </main>

        {/* #region footer-version */}
        {/* The running build and its commit, linked to GitHub (ADR-005). */}
        {build && (
          <footer className={styles.footer} data-frame="footer">
            <span data-testid="build-version">
              {build.version === 'dev' ? 'dev build' : `v${build.version}`}
            </span>
            {build.commit !== 'local' && (
              <>
                <span aria-hidden="true">·</span>
                <a
                  className={styles.footerCommit}
                  href={`https://github.com/SteveStout/TheYard/commit/${build.commit}`}
                  target="_blank"
                  rel="noreferrer"
                >
                  {build.commit}
                </a>
              </>
            )}
          </footer>
        )}
        {/* #endregion footer-version */}
      </div>
    </div>
  );
}
