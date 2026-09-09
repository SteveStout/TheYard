/**
 * The Store bar's seam (ADR: One container, both stores, and its addendum on
 * the toggle moving to the sites). Every container runs both stores, and
 * each site is one store's site: the live site is the relational store's by
 * default, the second site the document store's. The bar shows both, marks
 * the site the visitor is on, and makes the other segment a link to the other
 * site at the same path and query, so the address bar changes and nothing is
 * cached across a store. There is no in-place switch: a page whose every
 * number came from one store does not pretend to be the other.
 */

export interface Store {
  /** The short name the server uses: "sql" or "cosmos". */
  key: string;
  /** The store as it describes itself: "Azure SQL Database", "SQLite", "Azure Cosmos DB". */
  name: string;
  /** Whether the store came up. A store that did not still serves the catalogue from files, with no accounts. */
  ready: boolean;
  /** The store a request gets when it names none, which is what makes this container one store's site. */
  default: boolean;
}

export interface Stores {
  /** The store serving this request: the default, unless a measurement's header named the other. */
  current: string;
  stores: Store[];
  /** The other site, the container whose default is the other store, as a visitor reaches it; null where there is none. */
  other_site?: string | null;
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

/**
 * The two names a visitor sees on the bar, side by side, whichever site they
 * are on. Both families are always drawn so the choice reads as a choice; a
 * site this container cannot name is drawn as not here rather than left out,
 * because a bar with one segment is a label.
 */
export const FAMILIES = [
  { key: 'sql', label: 'SQL' },
  { key: 'cosmos', label: 'Cosmos DB' },
] as const;

export type Segment = {
  key: string;
  label: string;
  /** What the segment is: this site, the other site by its host, or a site that is not here. */
  title: string;
  /** Whether this segment is the site the visitor is on. */
  current: boolean;
  /** The other site at the visitor's own path and query, or null when the segment is not a link. */
  href: string | null;
};

/** Where the visitor is, as the bar needs it: the path and the query to carry to the other site. */
export type Here = { pathname: string; search: string };

/** The store this container serves by default, which is the store this site is named for. */
export function siteStore(stores: Stores): Store | undefined {
  return stores.stores.find((store) => store.default) ?? stores.stores[0];
}

/**
 * The sentence beside the bar: which site this is, which store is serving
 * the page, and that accounts and bids live in the store they were made in.
 * A store that did not come up still serves this page, from files, and the
 * bar says so rather than announcing a store with no accounts behind it as
 * though it were whole.
 */
export function note(stores: Stores): string {
  const current = stores.stores.find((store) => store.key === stores.current);
  if (!current) return '';
  if (!current.ready) {
    return `${current.name} did not come up on this container. The catalogue is served from files and there are no accounts on this store until it does.`;
  }
  const site = siteStore(stores);
  const family = FAMILIES.find((candidate) => candidate.key === site?.key);
  const which = family
    ? `This is the ${family.label} site, served from ${current.name}.`
    : `This page is served from ${current.name}.`;
  return `${which} Accounts and bids live in the store they were made in.`;
}

/**
 * The other site, when the server names one: its origin and the host to show,
 * because the host is what tells the two sites apart. An address the browser
 * cannot parse, or one that is not http or https, is not a link.
 */
export function otherSite(stores: Stores): { href: string; host: string } | null {
  if (!stores.other_site) return null;
  try {
    const url = new URL(stores.other_site);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null;
    return { href: url.origin, host: url.host };
  } catch {
    return null;
  }
}

/**
 * The other site at the same place: `/?view=admin` on one site lands on
 * `/?view=admin` on the other, so a reader comparing the two Admin tabs is
 * one click apart, not one click and a navigation.
 */
export function otherSiteAt(stores: Stores, here: Here): string | null {
  const other = otherSite(stores);
  if (!other) return null;
  return `${other.href}${here.pathname}${here.search}`;
}

/** What each segment of the bar says and does, from the server's answer and the visitor's own address. */
export function segments(stores: Stores, here: Here): Segment[] {
  const site = siteStore(stores);
  const other = otherSite(stores);
  const href = otherSiteAt(stores, here);
  return FAMILIES.map((family) => {
    if (site && site.key === family.key) {
      return {
        key: family.key,
        label: family.label,
        title: `This site. Its store is ${site.name}.`,
        current: true,
        href: null,
      };
    }
    if (other && href) {
      return {
        key: family.key,
        label: family.label,
        title: `The ${family.label} site, ${other.host}, at this same page`,
        current: false,
        href,
      };
    }
    return {
      key: family.key,
      label: family.label,
      title: `There is no ${family.label} site here: this container names no other site`,
      current: false,
      href: null,
    };
  });
}
// #endregion stores-seam
