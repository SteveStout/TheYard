import { createContext, useContext, useSyncExternalStore } from 'react';
import type { Account } from '../lib/auth';
import type { InventoryFacets, VehiclePage } from '../lib/data';
import type { Vehicle } from '../lib/types';

// #region first-load
/**
 * What the rendering service read for a page before it drew it (ADR: A rendering
 * service beside the API). The service draws the page with these answers, and
 * writes them into the page beside the markup, so the browser's first draw starts
 * from the same answers and React takes the markup over instead of replacing it.
 * A page the service did not draw has none, and every hook starts as it always did.
 */
export type FirstLoad = {
  /** The address's query as the request named it, "" for the bare address. */
  search: string;
  /** The service's clock when it drew the page: every countdown on it reads this, then ticks. */
  nowMs: number;
  /** Who is signed in, read with the visitor's own cookie. */
  account: Account;
  /** The build the API reports, for the version line. */
  build: { version: string; commit: string } | null;
  /** The first page of the list and the filter options, when the address opens the inventory. */
  listing: { page: VehiclePage; facets: InventoryFacets } | null;
  /** The vehicle a ?vehicle= address names, or null. */
  vehicle: Vehicle | null;
  /** A document a ?doc= address names, already rendered to HTML, or null. */
  doc: { key: string; html: string } | null;
};

/** Holds the first load for the page's first draw; null on a page drawn in the browser. */
export const FirstLoadContext = createContext<FirstLoad | null>(null);

/** The first load, or null when the page was not drawn by the rendering service. */
export function useFirstLoad(): FirstLoad | null {
  return useContext(FirstLoadContext);
}

const nothingToWatch = () => () => {};

/**
 * False on a server and while the browser takes a drawn page over, true from
 * then on and on every page the browser draws itself. A view that only the
 * browser draws (Admin, the account page) shows its placeholder until this is
 * true, so the server's draw and the browser's first draw agree.
 */
export function useInTheBrowser(): boolean {
  return useSyncExternalStore(
    nothingToWatch,
    () => true,
    () => false
  );
}
// #endregion first-load
