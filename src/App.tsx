import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import {
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  type InventoryFilters,
  type SortKey,
} from './lib/inventory';
import { applyBidRecord, useBids } from './hooks/useBids';
import { useNow } from './hooks/useNow';
import { AdminPanel } from './components/AdminPanel';
import { AccountPanel } from './components/AccountPanel';
import { fetchAccount, SIGNED_OUT, type Account } from './lib/auth';
import { readRailCollapsed, SideNav, storeRailCollapsed } from './components/SideNav';
import { docKeyForSlug, docSlug, type DocKey } from './components/DocsMenu';
import { BrandMark } from './components/BrandMark';
import { useMediaQuery } from './hooks/useMediaQuery';
import { FilterBar } from './components/FilterBar';
import { InventoryGrid } from './components/InventoryGrid';
import { VehicleDetail } from './components/VehicleDetail';
import styles from './App.module.css';

type LoadState = 'loading' | 'ready' | 'error';

/** How long to let the user keep typing/clicking before asking the API to filter. */
const FILTER_DEBOUNCE_MS = 500;

/** A status-filtered list with nothing left to cross still drifts; refresh this often. */
const STATUS_REFRESH_MS = 60_000;

/** The most often a listing will re-ask, however fast its auctions are ending. */
const LISTING_REFRESH_FLOOR_MS = 15_000;

/** Asked a moment after the boundary, so the server has crossed it too. */
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

/** Filters arrive in the URL (?make=Ford&status=live) so views are shareable. */
const INITIAL_PARAMS = new URLSearchParams(window.location.search);
const INITIAL_URL_STATE = filtersFromSearchParams(INITIAL_PARAMS);
/** A tile click is GET navigation: ?vehicle={id} deep-links the detail view. */
const INITIAL_VEHICLE_ID = INITIAL_PARAMS.get('vehicle');
/** ?doc=adr-lockout, resolved once. An address that names nothing opens nothing. */
const INITIAL_DOC = docKeyForSlug(INITIAL_PARAMS.get('doc'));

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
  /** The account view (ADR: Accounts and per-user bids), ?view=account. */
  const [accountOpen, setAccountOpen] = useState(INITIAL_PARAMS.get('view') === 'account');
  const [openDocKey, setOpenDocKey] = useState<DocKey | null>(INITIAL_DOC);
  const [account, setAccount] = useState<Account>(SIGNED_OUT);
  const now = useNow();
  /** The running build, reported by the container itself (ADR-005). */
  const [build, setBuild] = useState<{ version: string; commit: string } | null>(null);
  // #region docking
  /** The sidebar (ADR-013): a docked rail at 1024px and up, a drawer below. */
  const docked = useMediaQuery('(min-width: 1024px)');
  const [railCollapsed, setRailCollapsed] = useState(readRailCollapsed);
  const [drawerOpen, setDrawerOpen] = useState(false);
  useEffect(() => {
    storeRailCollapsed(railCollapsed);
  }, [railCollapsed]);
  // A window widened past the docking line has no drawer to keep open.
  useEffect(() => {
    if (docked) setDrawerOpen(false);
  }, [docked]);
  // #endregion docking
  // Bid state lives in the API; refetch the list whenever it changes.
  const refreshList = useCallback(() => setReloadNonce((n) => n + 1), []);
  // Keyed on the address: the bid map belongs to an account, so signing in or
  // out has to fetch a different one rather than keep showing the old badges.
  const { bids, placeBid, buyNow, resetBids } = useBids(refreshList, account.email);

  // #region visible-order
  /**
   * The current page with the buyer's bids layered on for instant feedback,
   * and, under the ending-soonest sort, reordered on the browser's clock.
   *
   * The reorder is the same ranking the API applied, re-applied to the page it
   * already sent. Re-asking the server on a timer was not enough and could not
   * be: over a hundred thousand auctions the soonest one ends inside a second,
   * so every answer's first row is expiring as it paints, and a shorter
   * interval only means asking more often for a page with the same problem.
   * The browser holds the one thing the response does not, which is the time
   * now, so it moves an auction that has ended to where the server would have
   * put it. Membership, the count and the paging stay the server's, and any
   * other sort is left exactly as it arrived.
   */
  const visibleVehicles = useMemo(() => {
    const withBids = page.vehicles.map((vehicle) => applyBidRecord(vehicle, bids[vehicle.id]));
    return sort === 'ending-soonest' ? byAuctionUrgency(withBids, now) : withBids;
  }, [page.vehicles, bids, sort, now]);
  // #endregion visible-order

  // Dropdown options come from the API (the page only ever holds a slice of
  // the dataset). Missing facets degrade to empty dropdowns, not a crash.
  useEffect(() => {
    const controller = new AbortController();
    fetchFacets(controller.signal)
      .then(setFacets)
      .catch(() => {});
    return () => controller.abort();
  }, [reloadNonce]);

  // The footer's version line: ask the running API which build it is.
  useEffect(() => {
    fetch('/api/version')
      .then((r) => (r.ok ? r.json() : null))
      .then(setBuild)
      .catch(() => {});
  }, []);

  // #region who
  // Who is signed in, if anyone. The session is an httpOnly cookie, so the
  // page cannot read it and has to ask (ADR: Accounts and per-user bids). A
  // failure here leaves the visitor signed out, which is the safe answer.
  useEffect(() => {
    fetchAccount()
      .then(setAccount)
      .catch(() => {});
  }, []);
  // #endregion who

  // Filtering, sorting, and paging are server-side: every change becomes a
  // GET request, debounced so typing doesn't spam the API and cached per
  // query string in data.ts. Cache hits skip the debounce entirely, because it
  // exists to simulate not hammering the server. reloadNonce bumps are
  // refreshes (retry buttons, the status interval): immediate and uncached.
  const lastNonce = useRef(reloadNonce);
  const initialAttempts = useRef(0);
  useEffect(() => {
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters, sort, reloadNonce]);

  // #region url-mirror
  // Mirror the current view into the address bar: the filter GET parameters
  // plus ?vehicle={id} when a detail page is open. replaceState here, because
  // typing shouldn't pile up history entries; opening a tile pushes its own
  // entry (below) so the browser's Back button closes the detail view.
  const deepLinkPending = useRef(INITIAL_VEHICLE_ID !== null);
  useEffect(() => {
    if (deepLinkPending.current) return;
    const params = filtersToSearchParams(filters, sort);
    if (selectedVehicle) params.set('vehicle', selectedVehicle.id);
    if (adminOpen) params.set('view', 'admin');
    if (accountOpen) params.set('view', 'account');
    if (openDocKey) params.set('doc', docSlug(openDocKey));
    const query = params.toString();
    window.history.replaceState(
      window.history.state,
      '',
      query ? `?${query}` : window.location.pathname
    );
  }, [filters, sort, selectedVehicle, adminOpen, accountOpen, openDocKey]);
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
  // Browser Back/Forward: re-read the whole view from the URL.
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
      setAccountOpen(params.get('view') === 'account');
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
  // A listing is answered once and then watched for minutes
  // (ADR: The listing that went stale while you looked at it). Auctions cross
  // their boundaries while it is on screen: a card's countdown reaches zero,
  // the browser turns it into an "Ended" chip, correctly, and it stays in the
  // position the server ranked it in while it was live. Under the default
  // sort, ending soonest, that position is the top of the front page, so a
  // minute after loading the first thing a visitor sees is dead lots.
  //
  // So the list is re-asked at the next moment its answer can have changed,
  // which is the soonest start or end still ahead of it, floored so that a page
  // where something ends every few seconds asks a few times a minute rather
  // than a few times a second. With nothing left to cross and a status filter
  // on, membership still drifts as auctions elsewhere end, and that keeps the
  // slower timer it always had.
  //
  // Not while the tab is hidden. Nobody is reading a stale card they cannot
  // see, and a background tab that refetches on a timer for an hour is the kind
  // of thing that gets noticed in somebody else's battery graph. The refresh
  // that was skipped happens when the tab comes back.
  const missedRefresh = useRef(false);
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
      if (document.hidden) {
        missedRefresh.current = true;
        return;
      }
      setReloadNonce((n) => n + 1);
    }, delay);
    return () => window.clearTimeout(id);
  }, [page, filters.status]);
  // #endregion listing-goes-stale

  // Having bid is not the same as leading any more (ADR-027): the room may
  // have answered. The server decides which it is; this only reads the answer.
  // #region refresh-open-vehicle
  // current_bid can be overlaid in the browser; min_next_bid cannot, because
  // the increment tiers are domain rules and live only on the server. So when
  // the room's figure on the open vehicle moves, the snapshot is refetched
  // rather than patched. Without this the panel said "someone outbid you, the
  // bid stands at $12,500" directly above "minimum $12,500", and submitting
  // the number the page showed was rejected.
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

  /** Appends the next server page; filters/sort changes reset via the fetch effect. */
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
  const backToInventory = () => {
    // If we pushed this entry, going back keeps history clean; a deep-linked
    // visit has no list entry behind it, so just swap the URL in place.
    if ((window.history.state as { viaTile?: boolean } | null)?.viaTile) {
      window.history.back();
      return;
    }
    setSelectedVehicle(null);
    const query = filtersToSearchParams(filters, sort).toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };
  // #endregion history

  useEffect(() => {
    // Admin is a view switch the same as a vehicle is. Left out of this, it
    // opened at the list's scroll offset with its heading above the fold and
    // focus on something the visitor could not see.
    window.scrollTo(0, selectedVehicle || adminOpen || accountOpen ? 0 : listScrollY.current);
  }, [selectedVehicle, adminOpen, accountOpen]);

  const openAdmin = () => {
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'admin');
    window.history.pushState({ viaAdmin: true }, '', `?${params}`);
    setSelectedVehicle(null);
    setAccountOpen(false);
    setAdminOpen(true);
  };

  // #region open-document
  // A record is a view, and every other view here is a GET parameter, so this
  // is the same shape as Admin and Account (ADR: A record with no address).
  // Before this, a decision record could only be reached by opening the site,
  // finding the group and scrolling, which means it could not be sent to
  // anybody, which for a project whose central artifact is its records is the
  // wrong way round.
  const openDocument = (key: DocKey | null) => {
    if (key !== null) {
      const params = filtersToSearchParams(filters, sort);
      params.set('doc', docSlug(key));
      window.history.pushState({ viaDoc: true }, '', `?${params}`);
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
    window.history.pushState({ viaAccount: true }, '', `?${params}`);
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
    const query = filtersToSearchParams(filters, sort).toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };
  // #endregion open-account
  const closeAdmin = () => {
    if ((window.history.state as { viaAdmin?: boolean } | null)?.viaAdmin) {
      window.history.back();
      return;
    }
    setAdminOpen(false);
    const query = filtersToSearchParams(filters, sort).toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };

  /** The sidebar's brand block: home is the inventory list, whatever is showing. */
  const goHome = () => {
    if (adminOpen) {
      closeAdmin();
      return;
    }
    if (accountOpen) {
      closeAccount();
      return;
    }
    backToInventory();
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
        {/* #region header-below-dock */}
        {/* Below the docking line the header carries the brand, Reset bids,
            and the hamburger; the docked rail makes it redundant above it. */}
        {!docked && (
          <header className={styles.header}>
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
                <button
                  type="button"
                  className={styles.hamburger}
                  aria-label="Menu"
                  aria-haspopup="dialog"
                  aria-expanded={drawerOpen}
                  onClick={() => setDrawerOpen(true)}
                >
                  <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true">
                    <path
                      d="M3 5h14M3 10h14M3 15h14"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                    />
                  </svg>
                </button>
              </div>
            </div>
          </header>
        )}
        {/* #endregion header-below-dock */}

        <main className={styles.main} id="main-content" ref={mainRef} tabIndex={-1}>
          <p className={styles.srOnly} role="status" data-testid="view-announcement">
            {announcement}
          </p>
          {adminOpen ? (
            <AdminPanel onBack={closeAdmin} />
          ) : accountOpen ? (
            <AccountPanel
              account={account}
              onAccountChange={setAccount}
              onOpenVehicle={openVehicleById}
              onBack={closeAccount}
            />
          ) : loadState === 'loading' ? (
            <p className={styles.notice} role="status">
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
              onPlaceBid={handlePlaceBid}
              onBuyNow={handleBuyNow}
            />
          ) : (
            <section aria-label="Vehicle inventory">
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
        {build && (
          <footer className={styles.footer}>
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
