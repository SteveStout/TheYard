/**
 * The store toggle's seam (ADR: One container, both stores). A container can
 * run the relational store and the document store side by side; which one
 * serves a request is a cookie the server sets. The page asks which stores
 * there are and which one it is on, and switching is one POST followed by a
 * full reload, because every number the page holds was read from the other
 * store and a cache of the wrong store's answers is worse than a cold page.
 */

export interface Store {
  /** The short name the cookie carries: "sql" or "cosmos". */
  key: string;
  /** The store as it describes itself: "Azure SQL Database", "SQLite", "Azure Cosmos DB". */
  name: string;
  /** Whether the store came up. A store that did not still serves the catalogue from files, with no accounts. */
  ready: boolean;
  /** The store a request gets when it names none. */
  default: boolean;
}

export interface Stores {
  current: string;
  stores: Store[];
}

// #region stores-seam
export async function fetchStores(signal?: AbortSignal): Promise<Stores | null> {
  try {
    const response = await fetch('/api/stores', { signal, credentials: 'same-origin' });
    if (!response.ok) return null;
    return (await response.json()) as Stores;
  } catch {
    // A page that cannot learn its stores is still a page; the bar just does
    // not draw, which is the honest shape for an API that is not answering.
    return null;
  }
}

export type SelectResult = { ok: true; stores: Stores } | { ok: false; message: string };

export async function selectStore(key: string): Promise<SelectResult> {
  let response: Response;
  try {
    response = await fetch('/api/stores/select', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ store: key }),
    });
  } catch {
    return { ok: false, message: 'The server could not be reached. Try again in a moment.' };
  }
  if (!response.ok) {
    let detail = 'That store could not be chosen.';
    try {
      const body = (await response.json()) as { detail?: unknown };
      if (typeof body.detail === 'string' && body.detail.length > 0) detail = body.detail;
    } catch {
      // The fallback sentence stands.
    }
    return { ok: false, message: detail };
  }
  return { ok: true, stores: (await response.json()) as Stores };
}

/**
 * The two names a visitor sees on the toggle, side by side, whichever stores
 * this container has. The toggle always shows both families so the choice
 * reads as a choice, and a family this container does not run is drawn as
 * such rather than left out: a toggle with one option is a label.
 */
export const FAMILIES = [
  { key: 'sql', label: 'SQL' },
  { key: 'cosmos', label: 'Cosmos DB' },
] as const;

export type Segment = {
  key: string;
  label: string;
  /** The store's own name, or a sentence about why it cannot be chosen. */
  title: string;
  current: boolean;
  /** Whether clicking it does anything. */
  available: boolean;
};

/**
 * The sentence beside the toggle. A store that did not come up still serves
 * this page, from files, and the bar says so rather than announcing a store
 * with no accounts behind it as though it were whole.
 */
export function note(stores: Stores): string {
  const current = stores.stores.find((store) => store.key === stores.current);
  if (!current) return '';
  if (!current.ready) {
    return `${current.name} did not come up on this container. The catalogue is served from files and there are no accounts on this store until it does.`;
  }
  return `This page is served from ${current.name}. Accounts and bids live in the store they were made in.`;
}

/** What each segment of the toggle says and does, from the server's answer. */
export function segments(stores: Stores): Segment[] {
  return FAMILIES.map((family) => {
    const store = stores.stores.find((candidate) => candidate.key === family.key);
    if (!store) {
      return {
        key: family.key,
        label: family.label,
        title: `${family.label} is not on this container`,
        current: false,
        available: false,
      };
    }
    return {
      key: family.key,
      label: family.label,
      title: store.ready
        ? store.name
        : `${store.name} is unavailable; the catalogue is served from files`,
      current: stores.current === store.key,
      available: store.ready && stores.current !== store.key,
    };
  });
}
// #endregion stores-seam
