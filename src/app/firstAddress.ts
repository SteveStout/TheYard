/**
 * Does:      Reads the address the page opened on into the views it names: the filters, the sort, the vehicle, the Admin
 *            card, the document, the blog, the review route and the reset token.
 * Does not:  Keep any of them or write the address bar (useAddressBar.ts does both).
 * Used by:   useAddressBar.ts, loaders.ts, render.ts.
 */
import { filtersFromSearchParams, opensInventory } from '../lib/inventory';
import { cardFromAddress, pinFromAddress } from '../lib/workbench';
import { docKeyForSlug } from '../library/addresses';
import { FILTER_PARTS } from '../lib/blogFilter';

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
  const inventory = inventoryPart(params);
  return {
    /** Filters live in it (?make=Ford&status=live). */
    ...filtersFromSearchParams(inventory),
    /** A tile click is GET navigation: ?vehicle={id} deep-links the detail view. */
    vehicleId: params.get('vehicle'),
    /** A password reset link's token: the address bar loses it on the first render. */
    resetToken: params.get('reset'),
    /** ?doc=adr-lockout opens that record. A name that matches no record opens nothing. */
    docKey: docKeyForSlug(params.get('doc')),
    /** Home is the landing page; an address naming the inventory, a filter, a sort or a vehicle opens the list. */
    inventory: opensInventory(inventory),
    admin: params.get('view') === 'admin',
    account: params.get('view') === 'account',
    /** The blog, ?view=blog, and one post on it, ?view=blog&post=solid-in-five-real-files. */
    blog: params.get('view') === 'blog',
    blogPost: blogPostFrom(params),
    /**
     * The open page's own parts of the address: the blog's list filter (?topic=, ?q=, ...) or the slide a post opens on
     * (?slide=3), or the review route's first screen (?for=ai).
     */
    pageQuery: pageQueryFrom(params),
    /** The review route, ?view=review (ADR: Ten minutes, seven stops). */
    review: params.get('view') === 'review',
    /** The recommendations page, ?view=recommendations (ADR: The recommendations have a page). */
    recommendations: params.get('view') === 'recommendations',
    /** The Admin card an address names (?view=admin&card=timing&pin=errors), resolved once. */
    card,
    pin: pinFromAddress(params.get('pin')),
  };
}

/**
 * The open page's own parts of the address, as a query: on the blog its list filter and a post's slide, on the review
 * route the first screen it was sent for (ADR: The review route gives a reason before it asks); "" anywhere else.
 */
export function pageQueryFrom(params: URLSearchParams): string {
  const own = new URLSearchParams();
  const view = params.get('view');
  const parts = view === 'blog' ? [...FILTER_PARTS, 'slide'] : view === 'review' ? ['for'] : [];
  for (const part of parts) {
    const value = params.get(part);
    if (value !== null && value !== '') own.set(part, value);
  }
  return own.toString();
}

/**
 * The inventory's part of an address: all of it, but nothing on the blog's page, whose q and sort are the blog's own
 * (?view=blog&q=onion&sort=read), so a blog address never filters the vehicles or opens the inventory under the blog.
 */
export function inventoryPart(params: URLSearchParams): URLSearchParams {
  return params.get('view') === 'blog' ? new URLSearchParams() : params;
}

/** The post a ?view=blog&post= address names, when its slug is one; null otherwise. */
export function blogPostFrom(params: URLSearchParams): string | null {
  const post = params.get('post');
  return params.get('view') === 'blog' && post !== null && /^[a-z0-9]+(-[a-z0-9]+)*$/.test(post)
    ? post
    : null;
}

/** The query the page opened on: the address bar in a browser, nothing where there is no window. */
export function browserSearch(): string {
  return typeof window === 'undefined' ? '' : window.location.search;
}
// #endregion first-address
