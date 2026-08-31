import type { AuctionStatus } from './auction';

/**
 * Inventory browsing state: the filter and sort model the UI edits. Both are
 * applied server-side via /api/vehicles GET parameters (see data.ts) — the
 * browser never filters or sorts the dataset itself.
 */

export interface InventoryFilters {
  /** Free-text search across year, make, model, trim, and every filterable field. */
  query: string;
  /** Empty string means "any" for the select-based filters. */
  make: string;
  bodyStyle: string;
  titleStatus: string;
  province: string;
  status: AuctionStatus | '';
  minCondition: number | null;
  /** Bounds apply to the price a buyer competes against (bid or opening ask). */
  priceMin: number | null;
  priceMax: number | null;
}

export const EMPTY_FILTERS: InventoryFilters = {
  query: '',
  make: '',
  bodyStyle: '',
  titleStatus: '',
  province: '',
  status: '',
  minCondition: null,
  priceMin: null,
  priceMax: null,
};

export type SortKey = 'ending-soonest' | 'price-asc' | 'price-desc' | 'condition' | 'most-bids';

export const SORT_OPTIONS: ReadonlyArray<{ value: SortKey; label: string }> = [
  { value: 'ending-soonest', label: 'Ending soonest' },
  { value: 'price-asc', label: 'Price: low to high' },
  { value: 'price-desc', label: 'Price: high to low' },
  { value: 'condition', label: 'Highest condition' },
  { value: 'most-bids', label: 'Most bids' },
];

/**
 * The browser-facing filter serialization — the same names the API takes, so
 * the address bar mirrors the API request. Used for both the URL bar and
 * (with the clock anchor appended) the actual fetch; see data.ts.
 */
export function filtersToSearchParams(
  filters: InventoryFilters,
  sort: SortKey = 'ending-soonest'
): URLSearchParams {
  const params = new URLSearchParams();
  const query = filters.query.trim();
  if (query) params.set('q', query);
  if (filters.make) params.set('make', filters.make);
  if (filters.bodyStyle) params.set('body_style', filters.bodyStyle);
  if (filters.titleStatus) params.set('title_status', filters.titleStatus);
  if (filters.province) params.set('province', filters.province);
  if (filters.status) params.set('status', filters.status);
  if (filters.minCondition !== null) params.set('min_condition', String(filters.minCondition));
  if (filters.priceMin !== null) params.set('price_min', String(filters.priceMin));
  if (filters.priceMax !== null) params.set('price_max', String(filters.priceMax));
  if (sort !== 'ending-soonest') params.set('sort', sort);
  return params;
}

/** Restores filter state from URL parameters, discarding anything invalid. */
export function filtersFromSearchParams(params: URLSearchParams): {
  filters: InventoryFilters;
  sort: SortKey;
} {
  const bound = (name: string): number | null => {
    const raw = params.get(name);
    if (raw === null || raw.trim() === '') return null;
    const value = Number(raw);
    return Number.isFinite(value) && value >= 0 ? value : null;
  };
  const status = params.get('status');
  const sort = params.get('sort');
  return {
    filters: {
      query: params.get('q') ?? '',
      make: params.get('make') ?? '',
      bodyStyle: params.get('body_style') ?? '',
      titleStatus: params.get('title_status') ?? '',
      province: params.get('province') ?? '',
      status: status === 'live' || status === 'upcoming' || status === 'ended' ? status : '',
      minCondition: bound('min_condition'),
      priceMin: bound('price_min'),
      priceMax: bound('price_max'),
    },
    sort: SORT_OPTIONS.some((option) => option.value === sort)
      ? (sort as SortKey)
      : 'ending-soonest',
  };
}

export function countActiveFilters(filters: InventoryFilters): number {
  let count = 0;
  if (filters.query.trim()) count++;
  if (filters.make) count++;
  if (filters.bodyStyle) count++;
  if (filters.titleStatus) count++;
  if (filters.province) count++;
  if (filters.status) count++;
  if (filters.minCondition !== null) count++;
  if (filters.priceMin !== null || filters.priceMax !== null) count++;
  return count;
}
