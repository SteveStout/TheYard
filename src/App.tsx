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
import {
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  type InventoryFilters,
  type SortKey,
} from './lib/inventory';
import { applyBidRecord, useBids } from './hooks/useBids';
import { useNow } from './hooks/useNow';
import { DocsMenu } from './components/DocsMenu';
import { FilterBar } from './components/FilterBar';
import { InventoryGrid } from './components/InventoryGrid';
import { VehicleDetail } from './components/VehicleDetail';
import styles from './App.module.css';

type LoadState = 'loading' | 'ready' | 'error';

/** How long to let the user keep typing/clicking before asking the API to filter. */
const FILTER_DEBOUNCE_MS = 500;

/** A status-filtered list goes stale as auctions cross their boundaries; refresh this often. */
const STATUS_REFRESH_MS = 60_000;

/** `npm start` opens the browser before the API finishes booting — keep
 *  retrying the first load quietly for a while before declaring an error. */
const INITIAL_RETRY_MS = 2_000;
const MAX_INITIAL_RETRIES = 15;

const EMPTY_FACETS: InventoryFacets = { makes: [], body_styles: [], title_statuses: [], provinces: [] };
const EMPTY_PAGE: VehiclePage = { total: 0, vehicles: [] };

/** Filters arrive in the URL (?make=Ford&status=live) so views are shareable. */
const INITIAL_PARAMS = new URLSearchParams(window.location.search);
const INITIAL_URL_STATE = filtersFromSearchParams(INITIAL_PARAMS);
/** A tile click is GET navigation: ?vehicle={id} deep-links the detail view. */
const INITIAL_VEHICLE_ID = INITIAL_PARAMS.get('vehicle');

export default function App() {
  /** The server-filtered, server-sorted page currently on display. */
  const [page, setPage] = useState<VehiclePage>(EMPTY_PAGE);
  const [facets, setFacets] = useState<InventoryFacets>(EMPTY_FACETS);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  /** A filter request failed — the list shows the previous results. */
  const [staleResults, setStaleResults] = useState(false);
  const [reloadNonce, setReloadNonce] = useState(0);
  /** Snapshot of the opened vehicle, so the detail view survives page refetches. */
  const [selectedVehicle, setSelectedVehicle] = useState<Vehicle | null>(null);
  const [filters, setFilters] = useState<InventoryFilters>(INITIAL_URL_STATE.filters);
  const [sort, setSort] = useState<SortKey>(INITIAL_URL_STATE.sort);
  const [loadingMore, setLoadingMore] = useState(false);
  const now = useNow();
  // Bid state lives in the API; refetch the list whenever it changes.
  const refreshList = useCallback(() => setReloadNonce((n) => n + 1), []);
  const { bids, placeBid, buyNow, resetBids } = useBids(refreshList);

  /** The current page with the buyer's bids layered on for instant feedback. */
  const visibleVehicles = useMemo(
    () => page.vehicles.map((vehicle) => applyBidRecord(vehicle, bids[vehicle.id])),
    [page.vehicles, bids]
  );

  // Dropdown options come from the API (the page only ever holds a slice of
  // the dataset). Missing facets degrade to empty dropdowns, not a crash.
  useEffect(() => {
    const controller = new AbortController();
    fetchFacets(controller.signal)
      .then(setFacets)
      .catch(() => {});
    return () => controller.abort();
  }, [reloadNonce]);

  // Filtering, sorting, and paging are server-side: every change becomes a
  // GET request, debounced so typing doesn't spam the API and cached per
  // query string in data.ts. Cache hits skip the debounce entirely — it only
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
              // first) — keep retrying quietly before showing the error.
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

  // Mirror the current view into the address bar: the filter GET parameters
  // plus ?vehicle={id} when a detail page is open. replaceState here —
  // typing shouldn't pile up history entries; opening a tile pushes its own
  // entry (below) so the browser's Back button closes the detail view.
  const deepLinkPending = useRef(INITIAL_VEHICLE_ID !== null);
  useEffect(() => {
    if (deepLinkPending.current) return;
    const params = filtersToSearchParams(filters, sort);
    if (selectedVehicle) params.set('vehicle', selectedVehicle.id);
    const query = params.toString();
    window.history.replaceState(
      window.history.state,
      '',
      query ? `?${query}` : window.location.pathname
    );
  }, [filters, sort, selectedVehicle]);

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

  // While a status filter is active, membership drifts as auctions open and
  // close — re-ask the server periodically so the list stays honest.
  useEffect(() => {
    if (!filters.status) return;
    const id = window.setInterval(() => setReloadNonce((n) => n + 1), STATUS_REFRESH_MS);
    return () => window.clearInterval(id);
  }, [filters.status]);

  const highBidderIds = useMemo(() => new Set(Object.keys(bids)), [bids]);
  const wonIds = useMemo(
    () => new Set(Object.entries(bids).filter(([, b]) => b.won_buy_now).map(([id]) => id)),
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

  useEffect(() => {
    window.scrollTo(0, selectedVehicle ? 0 : listScrollY.current);
  }, [selectedVehicle]);

  const patchFilters = (patch: Partial<InventoryFilters>) =>
    setFilters((prev) => ({ ...prev, ...patch }));
  const clearFilters = () => setFilters(EMPTY_FILTERS);

  const bidCount = Object.keys(bids).length;

  const handleResetBids = () => {
    if (window.confirm(`Clear your ${bidCount === 1 ? 'bid' : `${bidCount} bids`}? This can't be undone.`)) {
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

  return (
    <div className={styles.app}>
      <header className={styles.header}>
        <div className={styles.headerInner}>
          <button type="button" className={styles.brand} onClick={backToInventory}>
            <svg className={styles.brandMark} viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
              <path d="M13 2 5 14h5l-2 8 8-12h-5l2-8z" fill="currentColor" />
            </svg>
            The Block
            <span className={styles.brandSub}>Vehicle Auctions</span>
          </button>
          <div className={styles.headerActions}>
            <DocsMenu />
            {bidCount > 0 && (
              <button type="button" className={styles.resetBids} onClick={handleResetBids}>
                Reset bids ({bidCount})
              </button>
            )}
          </div>
        </div>
      </header>

      <main className={styles.main}>
        {loadState === 'loading' ? (
          <p className={styles.notice} role="status">
            Loading inventory…
          </p>
        ) : loadState === 'error' ? (
          <div className={styles.notice} role="alert">
            <p className={styles.noticeTitle}>Couldn't reach the inventory API.</p>
            <p>
              Make sure it's running — <code>npm run api</code> in a second terminal — then try
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
                Couldn't update results from the API — showing the previous list.
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
    </div>
  );
}
