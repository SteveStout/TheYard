import { describe, expect, it } from 'vitest';
import {
  countActiveFilters,
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  type InventoryFilters,
} from './inventory';

describe('countActiveFilters', () => {
  it('counts populated filters, treating the price pair as one', () => {
    expect(countActiveFilters(EMPTY_FILTERS)).toBe(0);
    expect(
      countActiveFilters({ ...EMPTY_FILTERS, make: 'Ford', priceMin: 1000, priceMax: 2000 })
    ).toBe(2);
    expect(
      countActiveFilters({ ...EMPTY_FILTERS, query: ' bronco ', status: 'live', minCondition: 3 })
    ).toBe(3);
  });
});

describe('URL filter state', () => {
  it('round-trips filters and sort through search params', () => {
    const filters: InventoryFilters = {
      ...EMPTY_FILTERS,
      query: 'bronco',
      make: 'Ford',
      status: 'live',
      minCondition: 3.5,
      priceMax: 30000,
    };

    const restored = filtersFromSearchParams(filtersToSearchParams(filters, 'price-asc'));
    expect(restored.filters).toEqual(filters);
    expect(restored.sort).toBe('price-asc');
  });

  it('produces an empty string for the default state', () => {
    expect(filtersToSearchParams(EMPTY_FILTERS).toString()).toBe('');
  });

  it('discards invalid URL values instead of crashing', () => {
    const restored = filtersFromSearchParams(
      new URLSearchParams('status=sideways&sort=alphabetical&price_min=abc&min_condition=-2')
    );
    expect(restored.filters.status).toBe('');
    expect(restored.filters.priceMin).toBeNull();
    expect(restored.filters.minCondition).toBeNull();
    expect(restored.sort).toBe('ending-soonest');
  });
});
