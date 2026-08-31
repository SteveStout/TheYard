import { useId } from 'react';
import {
  countActiveFilters,
  SORT_OPTIONS,
  type InventoryFilters,
  type SortKey,
} from '../lib/inventory';
import type { AuctionStatus } from '../lib/auction';
import { capitalize, formatInteger } from '../lib/format';
import styles from './FilterBar.module.css';

interface FilterBarProps {
  filters: InventoryFilters;
  onFiltersChange: (patch: Partial<InventoryFilters>) => void;
  sort: SortKey;
  onSortChange: (sort: SortKey) => void;
  onClear: () => void;
  makes: string[];
  bodyStyles: string[];
  titleStatuses: string[];
  provinces: string[];
  /** How many rows the current page shows. */
  shownCount: number;
  /** How many vehicles match the filters in total, server-side. */
  totalCount: number;
}

const CONDITION_OPTIONS = [
  { value: '', label: 'Any condition' },
  { value: '2', label: '2.0 and up' },
  { value: '3', label: '3.0 and up' },
  { value: '3.5', label: '3.5 and up' },
  { value: '4', label: '4.0 and up' },
  { value: '4.5', label: '4.5 and up' },
];

const STATUS_OPTIONS: Array<{ value: AuctionStatus | ''; label: string }> = [
  { value: '', label: 'Any status' },
  { value: 'live', label: 'Live' },
  { value: 'upcoming', label: 'Upcoming' },
  { value: 'ended', label: 'Ended' },
];

/** Parses a price input: empty or invalid → null; whole dollars only (the API binds int). */
function parseBound(value: string): number | null {
  if (value.trim() === '') return null;
  const n = Math.floor(Number(value));
  return Number.isFinite(n) && n >= 0 ? n : null;
}

export function FilterBar({
  filters,
  onFiltersChange,
  sort,
  onSortChange,
  onClear,
  makes,
  bodyStyles,
  titleStatuses,
  provinces,
  shownCount,
  totalCount,
}: FilterBarProps) {
  const id = useId();
  const activeCount = countActiveFilters(filters);

  return (
    <section className={styles.bar} aria-label="Search and filters">
      <div className={styles.topRow}>
        <div className={styles.search}>
          <svg className={styles.searchIcon} viewBox="0 0 20 20" width="16" height="16" aria-hidden="true">
            <path
              d="M9 3.5a5.5 5.5 0 1 0 3.4 9.82l3.64 3.64a.9.9 0 0 0 1.28-1.28l-3.64-3.64A5.5 5.5 0 0 0 9 3.5Zm-3.7 5.5a3.7 3.7 0 1 1 7.4 0 3.7 3.7 0 0 1-7.4 0Z"
              fill="currentColor"
            />
          </svg>
          <input
            type="search"
            className={styles.searchInput}
            placeholder="Search make, model, province, title…"
            aria-label="Search vehicles"
            value={filters.query}
            onChange={(e) => onFiltersChange({ query: e.target.value })}
          />
        </div>

        <label className={styles.sort}>
          <span className={styles.fieldLabel}>Sort by</span>
          <select
            className={styles.select}
            value={sort}
            onChange={(e) => onSortChange(e.target.value as SortKey)}
          >
            {SORT_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className={styles.fields}>
        <label className={styles.field}>
          <span className={styles.fieldLabel}>Make</span>
          <select
            className={styles.select}
            value={filters.make}
            onChange={(e) => onFiltersChange({ make: e.target.value })}
          >
            <option value="">All makes</option>
            {makes.map((make) => (
              <option key={make} value={make}>
                {make}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.field}>
          <span className={styles.fieldLabel}>Body style</span>
          <select
            className={styles.select}
            value={filters.bodyStyle}
            onChange={(e) => onFiltersChange({ bodyStyle: e.target.value })}
          >
            <option value="">All styles</option>
            {bodyStyles.map((style) => (
              <option key={style} value={style}>
                {capitalize(style)}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.field}>
          <span className={styles.fieldLabel}>Title</span>
          <select
            className={styles.select}
            value={filters.titleStatus}
            onChange={(e) => onFiltersChange({ titleStatus: e.target.value })}
          >
            <option value="">All titles</option>
            {titleStatuses.map((status) => (
              <option key={status} value={status}>
                {capitalize(status)}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.field}>
          <span className={styles.fieldLabel}>Province</span>
          <select
            className={styles.select}
            value={filters.province}
            onChange={(e) => onFiltersChange({ province: e.target.value })}
          >
            <option value="">All provinces</option>
            {provinces.map((province) => (
              <option key={province} value={province}>
                {province}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.field}>
          <span className={styles.fieldLabel}>Auction</span>
          <select
            className={styles.select}
            value={filters.status}
            onChange={(e) => onFiltersChange({ status: e.target.value as AuctionStatus | '' })}
          >
            {STATUS_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.field}>
          <span className={styles.fieldLabel}>Condition</span>
          <select
            className={styles.select}
            value={filters.minCondition === null ? '' : String(filters.minCondition)}
            onChange={(e) =>
              onFiltersChange({
                minCondition: e.target.value === '' ? null : Number(e.target.value),
              })
            }
          >
            {CONDITION_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <div className={styles.field}>
          <span className={styles.fieldLabel} id={`${id}-price`}>
            Price ($)
          </span>
          <div className={styles.priceInputs} role="group" aria-labelledby={`${id}-price`}>
            <input
              type="number"
              className={styles.priceInput}
              placeholder="Min"
              aria-label="Minimum price"
              min={0}
              step={500}
              value={filters.priceMin ?? ''}
              onChange={(e) => onFiltersChange({ priceMin: parseBound(e.target.value) })}
            />
            <span aria-hidden="true">–</span>
            <input
              type="number"
              className={styles.priceInput}
              placeholder="Max"
              aria-label="Maximum price"
              min={0}
              step={500}
              value={filters.priceMax ?? ''}
              onChange={(e) => onFiltersChange({ priceMax: parseBound(e.target.value) })}
            />
          </div>
        </div>
      </div>

      <div className={styles.footer}>
        <p className={styles.count} role="status">
          {totalCount > shownCount
            ? `Showing ${shownCount} of ${formatInteger(totalCount)} vehicles`
            : `${totalCount} ${totalCount === 1 ? 'vehicle' : 'vehicles'}`}
        </p>
        {activeCount > 0 && (
          <button type="button" className={styles.clear} onClick={onClear}>
            Clear filters ({activeCount})
          </button>
        )}
      </div>
    </section>
  );
}
