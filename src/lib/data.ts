import type { Vehicle } from './types';
import { filtersToSearchParams, type InventoryFilters, type SortKey } from './inventory';

/**
 * The single seam for API access. The .NET API (api/) owns the data AND the
 * rules: it serves paged envelopes { total, vehicles } with server-derived
 * auction facts on each vehicle, computes facets, and validates bids. In
 * dev, Vite proxies /api to http://localhost:5210 (see vite.config.ts), so
 * run `npm run api` alongside `npm run dev`.
 *
 * GET responses are cached in-memory per query string for a short TTL.
 * Mutations (bids, reset) clear the cache, since they change server data.
 */

export interface VehiclePage {
  total: number;
  vehicles: Vehicle[];
}

export interface InventoryFacets {
  makes: string[];
  body_styles: string[];
  title_statuses: string[];
  provinces: string[];
}

/** The buyer's standing on one vehicle, as the API reports it. */
export interface BidRecord {
  amount: number;
  bid_count: number;
  won_buy_now: boolean;
  /** When the bid was placed, which the simulated room reads (ADR-027). */
  at_ms: number;
  /** True when the room has since bid higher. The server decides this. */
  outbid: boolean;
  /** What the room is standing at, or null when it has not bid here. */
  market_amount: number | null;
}

export type BidMap = Record<string, BidRecord>;

export type BidOutcome =
  { kind: 'rejected'; reason: string } | { kind: 'accepted' | 'won'; amount: number };

export interface BidResult {
  outcome: BidOutcome;
  bid?: BidRecord;
  /** The vehicle with the bid applied and a fresh min_next_bid. */
  vehicle?: Vehicle;
}

// #region problem-detail
/**
 * Reads the message out of a failed response. The API answers RFC 9457
 * ProblemDetails, so `detail` is the field to trust (ADR: Error handling);
 * `reason` and `error` are the two shapes it used to answer with, kept here
 * so a browser holding an old bundle still shows a real message during a roll.
 */
async function problemDetail(response: Response, fallback: string): Promise<string> {
  // A failure can arrive without a JSON body at all: an edge or proxy error
  // page, or an empty 502. Anything unreadable falls back to the sentence the
  // caller supplied rather than throwing a second error over the first.
  let body: { detail?: string; reason?: string; error?: string } | null = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }
  return body?.detail ?? body?.reason ?? body?.error ?? fallback;
}
// #endregion problem-detail

const CACHE_TTL_MS = 5 * 60 * 1000;
const CACHE_MAX_ENTRIES = 30;

interface CacheEntry {
  at: number;
  page: VehiclePage;
}

/** Map preserves insertion order, so the first key is always the oldest. */
const queryCache = new Map<string, CacheEntry>();

export function clearVehicleCache(): void {
  queryCache.clear();
}

/** The buyer's local midnight, the anchor every schedule-dependent request carries. */
export function localMidnightMs(): number {
  const midnight = new Date();
  midnight.setHours(0, 0, 0, 0);
  return midnight.getTime();
}

/** Maps UI filter/sort state to the API's query parameters. Exported for tests. */
export function vehicleQueryParams(
  filters: InventoryFilters,
  sort: SortKey = 'ending-soonest'
): URLSearchParams {
  const params = filtersToSearchParams(filters, sort);
  // Every request carries the clock anchor: the status filter, text search
  // (tokens like "live"), the default auction-time sort, and the derived
  // fields on each vehicle all depend on it. Stable within a day, so cache
  // keys stay stable too. (The URL bar uses filtersToSearchParams directly,
  // without the anchor, because it's clock plumbing, not user state.)
  params.set('anchor_ms', String(localMidnightMs()));
  return params;
}

function cacheKey(filters?: InventoryFilters, sort?: SortKey, offset = 0): string {
  const params = filters ? vehicleQueryParams(filters, sort) : new URLSearchParams();
  if (offset > 0) params.set('offset', String(offset));
  return params.toString();
}

function cachedPage(key: string): VehiclePage | null {
  const hit = queryCache.get(key);
  return hit && Date.now() - hit.at < CACHE_TTL_MS ? hit.page : null;
}

/**
 * Synchronous cache peek. Callers use this to skip their request debounce on
 * a hit: the debounce only exists to avoid hammering the API, and a cached
 * result never touches the API.
 */
export function peekVehicles(filters?: InventoryFilters, sort?: SortKey): VehiclePage | null {
  return cachedPage(cacheKey(filters, sort));
}

// #region fetch-vehicles
// The one function every list view goes through. Three things share this seam:
// a cache keyed by the exact query string (a filter combination revisited
// renders instantly), an AbortSignal so an effect's cleanup can cancel a
// request the visitor has already typed past, and forceRefresh for the paths
// that must not read a cache (retry buttons, the periodic status refresh).
export interface FetchVehiclesOptions {
  sort?: SortKey;
  offset?: number;
  signal?: AbortSignal;
  /** Skip the cache and overwrite it with a fresh response. */
  forceRefresh?: boolean;
}

export async function fetchVehicles(
  filters?: InventoryFilters,
  { sort, offset = 0, signal, forceRefresh = false }: FetchVehiclesOptions = {}
): Promise<VehiclePage> {
  const key = cacheKey(filters, sort, offset);

  if (!forceRefresh) {
    const hit = cachedPage(key);
    if (hit) {
      return hit;
    }
  }

  const response = await fetch(`/api/vehicles${key ? `?${key}` : ''}`, { signal });
  if (!response.ok) {
    throw new Error(
      await problemDetail(response, `The inventory API responded with ${response.status}`)
    );
  }
  const page = (await response.json()) as VehiclePage;

  // Re-inserting moves the key to the back, so eviction drops the stalest.
  queryCache.delete(key);
  queryCache.set(key, { at: Date.now(), page });
  if (queryCache.size > CACHE_MAX_ENTRIES) {
    const oldest = queryCache.keys().next().value;
    if (oldest !== undefined) queryCache.delete(oldest);
  }
  return page;
}
// #endregion fetch-vehicles

/** One vehicle by id, or null when it doesn't exist. Backs detail deep links. */
export async function fetchVehicleById(id: string, signal?: AbortSignal): Promise<Vehicle | null> {
  const response = await fetch(
    `/api/vehicles/${encodeURIComponent(id)}?anchor_ms=${localMidnightMs()}`,
    { signal }
  );
  if (response.status === 404) return null;
  if (!response.ok) {
    throw new Error(`The inventory API responded with ${response.status}`);
  }
  return (await response.json()) as Vehicle;
}

/** Dropdown values, computed by the API over the full dataset. */
export async function fetchFacets(signal?: AbortSignal): Promise<InventoryFacets> {
  const response = await fetch('/api/facets', { signal });
  if (!response.ok) {
    throw new Error(`The inventory API responded with ${response.status}`);
  }
  return (await response.json()) as InventoryFacets;
}

// ---------------------------------------------------------------------------
// Bidding. Validation is server-side; these calls relay outcomes.
// ---------------------------------------------------------------------------

export async function fetchBids(signal?: AbortSignal): Promise<BidMap> {
  const response = await fetch('/api/bids', { signal });
  if (!response.ok) {
    throw new Error(`The inventory API responded with ${response.status}`);
  }
  return (await response.json()) as BidMap;
}

async function postBidAction(url: string, body: Record<string, unknown>): Promise<BidResult> {
  let response: Response;
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...body, anchor_ms: localMidnightMs() }),
    });
  } catch {
    return { outcome: { kind: 'rejected', reason: 'Could not reach the auction API.' } };
  }

  if (!response.ok) {
    const reason =
      response.status === 404
        ? 'This vehicle no longer exists.'
        : await problemDetail(response, `The auction API responded with ${response.status}.`);
    return { outcome: { kind: 'rejected', reason } };
  }

  const result = (await response.json()) as {
    kind: 'accepted' | 'won';
    amount: number;
    bid: BidRecord;
    vehicle: Vehicle;
  };
  // Server data changed, so cached pages are stale now.
  clearVehicleCache();
  return {
    outcome: { kind: result.kind, amount: result.amount },
    bid: result.bid,
    vehicle: result.vehicle,
  };
}

// #region tick
/**
 * One round of bidding by the simulated room (ADR-027). Driven by the page
 * rather than a timer on the server: the room moves while somebody is
 * watching, which is the only time it matters, and the anchor rides along
 * because the browser's midnight is what decides which auctions are live.
 *
 * Returns how many vehicles it raised and the buyer's refreshed map. A failure
 * is not worth surfacing: the room going quiet for eight seconds is invisible.
 */
export async function tickMarket(): Promise<{ raised: number; bids: BidMap } | null> {
  try {
    const response = await fetch('/api/market/tick', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ anchor_ms: localMidnightMs() }),
    });
    if (!response.ok) {
      return null;
    }
    const result = (await response.json()) as { raised: number; bids: BidMap };
    if (result.raised > 0) {
      // Somebody else's bids changed the figures, so cached pages are stale.
      clearVehicleCache();
    }
    return result;
  } catch {
    return null;
  }
}
// #endregion tick

export function placeBidRequest(vehicleId: string, amount: number): Promise<BidResult> {
  return postBidAction(`/api/vehicles/${vehicleId}/bids`, { amount });
}

export function buyNowRequest(vehicleId: string): Promise<BidResult> {
  return postBidAction(`/api/vehicles/${vehicleId}/buy-now`, {});
}

export async function resetBidsRequest(): Promise<void> {
  const response = await fetch('/api/bids', { method: 'DELETE' });
  if (!response.ok) {
    throw new Error(`The inventory API responded with ${response.status}`);
  }
  clearVehicleCache();
}
