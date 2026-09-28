/**
 * Does:      Draws the inventory page: its title, the filter bar, the grid, Load more, and what shows while the list loads or when the API cannot be reached.
 * Does not:  Fetch the list (useInventory.ts does), keep the filters (useAddressBar.ts does), or draw a vehicle's own page.
 * Used by:   App.tsx.
 */
import type { Vehicle } from '../lib/types';
import type { InventoryFilters, SortKey } from '../lib/inventory';
import { FilterBar } from '../components/inventory/FilterBar';
import { InventoryGrid } from '../components/inventory/InventoryGrid';
import type { Inventory } from './hooks/useInventory';
import styles from './App.module.css';

export function InventoryView({
  inventory,
  filters,
  onFiltersChange,
  onClearFilters,
  sort,
  onSortChange,
  now,
  onSelect,
}: {
  inventory: Inventory;
  filters: InventoryFilters;
  onFiltersChange: (patch: Partial<InventoryFilters>) => void;
  onClearFilters: () => void;
  sort: SortKey;
  onSortChange: (sort: SortKey) => void;
  now: number;
  onSelect: (vehicle: Vehicle) => void;
}) {
  const { page, facets, visibleVehicles } = inventory;
  return (
    <section aria-label="Vehicle inventory">
      {/* No welcome banner here: the landing page owns that copy. */}
      <div className={styles.listHeader}>
        <h1 className={styles.listTitle}>Inventory</h1>
      </div>
      <FilterBar
        filters={filters}
        onFiltersChange={onFiltersChange}
        sort={sort}
        onSortChange={onSortChange}
        onClear={onClearFilters}
        makes={facets.makes}
        bodyStyles={facets.body_styles}
        titleStatuses={facets.title_statuses}
        provinces={facets.provinces}
        shownCount={visibleVehicles.length}
        totalCount={page.total}
      />
      {inventory.staleResults && (
        <div className={styles.staleBanner} role="alert">
          Couldn't update results from the API. Showing the previous list.
          <button type="button" className={styles.staleRetry} onClick={inventory.refresh}>
            Retry
          </button>
        </div>
      )}
      <InventoryGrid
        vehicles={visibleVehicles}
        now={now}
        onSelect={onSelect}
        highBidderIds={inventory.highBidderIds}
        outbidIds={inventory.outbidIds}
        wonIds={inventory.wonIds}
        onClearFilters={onClearFilters}
      />
      {page.vehicles.length < page.total && (
        <div className={styles.loadMoreRow}>
          <button
            type="button"
            className={styles.loadMore}
            onClick={() => void inventory.loadMore()}
            disabled={inventory.loadingMore}
          >
            {inventory.loadingMore ? 'Loading…' : 'Load more vehicles'}
          </button>
        </div>
      )}
    </section>
  );
}

/** What shows while the first page of the list is on its way. */
export function InventoryLoading() {
  return (
    <p className={`${styles.notice} ${styles.loadingInventory}`} role="status">
      Loading inventory…
    </p>
  );
}

/** What shows when the API could not be reached, with the one thing to try. */
export function InventoryUnreachable({ onRetry }: { onRetry: () => void }) {
  return (
    <div className={styles.notice} role="alert">
      <p className={styles.noticeTitle}>Couldn't reach the inventory API.</p>
      <p>
        Make sure it's running (<code>npm run api</code> in a second terminal), then try again.
      </p>
      <button type="button" className={styles.retry} onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}
