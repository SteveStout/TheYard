import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearVehicleCache, fetchVehicles, peekVehicles, vehicleQueryParams } from './data';
import { EMPTY_FILTERS } from './inventory';

describe('vehicleQueryParams', () => {
  it('produces only the clock anchor for empty filters and the default sort', () => {
    const params = vehicleQueryParams(EMPTY_FILTERS);
    expect(params.size).toBe(1);
    const midnight = new Date();
    midnight.setHours(0, 0, 0, 0);
    expect(params.get('anchor_ms')).toBe(String(midnight.getTime()));
  });

  it('maps every populated filter to its API parameter name', () => {
    const params = vehicleQueryParams(
      {
        query: ' bronco ',
        make: 'Ford',
        bodyStyle: 'SUV',
        titleStatus: 'clean',
        province: 'Ontario',
        status: 'live',
        minCondition: 3.5,
        priceMin: 10000,
        priceMax: 30000,
      },
      'price-asc'
    );

    expect(params.get('q')).toBe('bronco');
    expect(params.get('make')).toBe('Ford');
    expect(params.get('body_style')).toBe('SUV');
    expect(params.get('title_status')).toBe('clean');
    expect(params.get('province')).toBe('Ontario');
    expect(params.get('status')).toBe('live');
    expect(params.get('min_condition')).toBe('3.5');
    expect(params.get('price_min')).toBe('10000');
    expect(params.get('price_max')).toBe('30000');
    expect(params.get('sort')).toBe('price-asc');
  });

  it('omits the sort parameter for the default sort', () => {
    expect(vehicleQueryParams(EMPTY_FILTERS, 'ending-soonest').has('sort')).toBe(false);
  });

  it('omits a whitespace-only search query', () => {
    expect(vehicleQueryParams({ ...EMPTY_FILTERS, query: '   ' }).has('q')).toBe(false);
  });
});

describe('fetchVehicles caching', () => {
  const okResponse = () =>
    ({ ok: true, json: async () => ({ total: 1, vehicles: [{ id: 'v1' }] }) }) as unknown as Response;

  beforeEach(() => {
    clearVehicleCache();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-08-16T12:00:00'));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('serves a repeated query from the cache', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await fetchVehicles();
    await fetchVehicles();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('caches per query-parameter combination, including sort', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await fetchVehicles({ ...EMPTY_FILTERS, make: 'Ford' });
    await fetchVehicles({ ...EMPTY_FILTERS, make: 'Ford' }, { sort: 'price-asc' });
    await fetchVehicles({ ...EMPTY_FILTERS, make: 'Ford' });

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('expires entries after the TTL', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await fetchVehicles();
    vi.advanceTimersByTime(5 * 60 * 1000 + 1);
    await fetchVehicles();

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('peekVehicles reports a hit synchronously and respects the TTL', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    expect(peekVehicles()).toBeNull();
    await fetchVehicles();
    expect(peekVehicles()?.vehicles).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(5 * 60 * 1000 + 1);
    expect(peekVehicles()).toBeNull();
  });

  it('bypasses the cache when a refresh is forced', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await fetchVehicles();
    await fetchVehicles(undefined, { forceRefresh: true });

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not cache failed responses', async () => {
    const fetchMock = vi
      .fn()
      .mockImplementationOnce(async () => ({ ok: false, status: 500 }) as unknown as Response)
      .mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await expect(fetchVehicles()).rejects.toThrow('500');
    await expect(fetchVehicles()).resolves.toMatchObject({ total: 1 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
