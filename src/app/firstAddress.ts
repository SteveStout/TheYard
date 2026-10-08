/**
 * Does:      Reads the address the page opened on into the views it names: the filters, the sort, the vehicle, the Admin
 *            card, the document and the reset token.
 * Does not:  Keep any of them or write the address bar (useAddressBar.ts does both).
 * Used by:   useAddressBar.ts, loaders.ts.
 */
import { filtersFromSearchParams, opensInventory } from '../lib/inventory';
import { cardFromAddress, pinFromAddress } from '../lib/workbench';
import { docKeyForSlug } from '../library/addresses';

// #region first-address
/**
 * The address the page opened on, read once. In the browser that is the
 * address bar. On the rendering service there is no window, so the service
 * hands over the query the request named (ADR: A rendering service beside the
 * API), and the build's landing page is the address with no query.
 * @param search the query, with or without its leading "?"
 */
export function readFirstAddress(search: string) {
  const params = new URLSearchParams(search);
  const card = cardFromAddress(params.get('card'));
  return {
    /** Filters live in it (?make=Ford&status=live). */
    ...filtersFromSearchParams(params),
    /** A tile click is GET navigation: ?vehicle={id} deep-links the detail view. */
    vehicleId: params.get('vehicle'),
    /** A password reset link's token: the address bar loses it on the first render. */
    resetToken: params.get('reset'),
    /** ?doc=adr-lockout opens that record. A name that matches no record opens nothing. */
    docKey: docKeyForSlug(params.get('doc')),
    /** Home is the landing page; an address naming the inventory, a filter, a sort or a vehicle opens the list. */
    inventory: opensInventory(params),
    admin: params.get('view') === 'admin',
    account: params.get('view') === 'account',
    /** The Admin card an address names (?view=admin&card=timing&pin=errors), resolved once. */
    card,
    pin: pinFromAddress(params.get('pin')),
  };
}

/** The query the page opened on: the address bar in a browser, nothing where there is no window. */
export function browserSearch(): string {
  return typeof window === 'undefined' ? '' : window.location.search;
}
// #endregion first-address
