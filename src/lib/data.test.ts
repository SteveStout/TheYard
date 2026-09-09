import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearVehicleCache, fetchVehicles, peekVehicles, utcDay, vehicleQueryParams } from './data';
import { EMPTY_FILTERS } from './inventory';

describe('vehicleQueryParams', () => {
  it('produces nothing at all for empty filters and the default sort', () => {
    // No clock anchor, since 1.0.0.112: the schedule is the server's, and a
    // request that named its own midnight was a request that named its own
    // auction (ADR: Three readers with no memory of the project, the addendum
    // on the clock).
    const params = vehicleQueryParams(EMPTY_FILTERS);
    expect(params.size).toBe(0);
    expect(params.has('anchor_ms')).toBe(false);
  });

  it('keys the cache by the UTC day, because the server re-seeds the windows at UTC midnight', () => {
    const lastMomentOfTheDay = Date.UTC(2026, 8, 9, 23, 59, 59, 999);
    expect(utcDay(lastMomentOfTheDay)).toBe(utcDay(Date.UTC(2026, 8, 9, 0, 0, 0)));
    expect(utcDay(lastMomentOfTheDay + 1)).toBe(utcDay(lastMomentOfTheDay) + 1);
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

// #region cache-tests
// The network is a counter here: vi.stubGlobal replaces fetch with a mock that
// answers one page, and fake timers let the five-minute TTL expire in a line.
// Each test asserts how many times the "network" was touched.
describe('fetchVehicles caching', () => {
  const okResponse = () =>
    ({
      ok: true,
      json: async () => ({ total: 1, vehicles: [{ id: 'v1' }] }),
    }) as unknown as Response;

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

  it('asks the API with the query string and never with the cache key', async () => {
    // The cache key carries the UTC day in front of the query string, and
    // the first cut of that sent the key as the URL: every listing request
    // went out as /api/vehicles?20706:offset=100, the server ignored what it
    // could not read, and Load more appended the first page again (the
    // 1.0.0.112 gate, take one).
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await fetchVehicles();
    await fetchVehicles({ ...EMPTY_FILTERS, make: 'Ford' }, { sort: 'price-asc', offset: 100 });

    expect(fetchMock.mock.calls[0][0]).toBe('/api/vehicles');
    expect(fetchMock.mock.calls[1][0]).toBe('/api/vehicles?make=Ford&sort=price-asc&offset=100');
  });

  it('forgets everything cached before UTC midnight once the day turns', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    vi.setSystemTime(new Date('2026-08-16T23:59:30Z'));
    await fetchVehicles();
    vi.setSystemTime(new Date('2026-08-17T00:00:30Z'));
    await fetchVehicles();

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
  // #endregion cache-tests

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
      .mockImplementationOnce(
        async () =>
          ({
            ok: false,
            status: 500,
            // A real Response always has json(); the API answers ProblemDetails
            // on a failure, so the stub does too (ADR: Error handling).
            json: async () => ({
              status: 500,
              title: 'Server error',
              detail: 'The API responded with 500',
            }),
          }) as unknown as Response
      )
      .mockImplementation(async () => okResponse());
    vi.stubGlobal('fetch', fetchMock);

    await expect(fetchVehicles()).rejects.toThrow('500');
    await expect(fetchVehicles()).resolves.toMatchObject({ total: 1 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
