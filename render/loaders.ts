/**
 * Does:      Reads from the API what an address needs drawn before it is drawn: who is signed in, the build, the first page
 *            of the list, the vehicle, the document, the blog's list and the post it shows in full. One loader per view, all at once, each under one deadline.
 * Does not:  Draw anything, read a store, or reach anything but the API's public reads; a read that fails or runs past the
 *            deadline is left to the browser, which asks for it as it always did.
 * Used by:   render.ts, render.test.ts.
 */
import type { FirstLoad } from '../src/hooks/useFirstLoad';
import { SIGNED_OUT, toAccount, type Account, type AccountWire } from '../src/lib/auth';
import { vehicleQueryParams, type InventoryFacets, type VehiclePage } from '../src/lib/data';
import { layoutDocument } from '../src/lib/docLayout';
import { renderDocument } from '../src/lib/markdown';
import type { Vehicle } from '../src/lib/types';
import { readFirstAddress } from '../src/app/firstAddress';
import { DOCS } from '../src/library/documents';
import { PULSE_URL } from '../src/lib/pulse';
import { RECOMMENDATIONS_URL } from '../src/lib/recommendations';
import { SELF_READ } from './page';
import { fetchedFor, type BlogList, type BlogPost } from '../src/lib/blog';
import { filterFromSearch, listPath } from '../src/lib/blogFilter';

// #region reads
/** One read of the API: its JSON, or null on anything but a 200. */
type Read = <T>(path: string, signal: AbortSignal) => Promise<T | null>;

/** The header that names the store a read is for, as the API names it (api/TheYard.Api/Shared/Stores/Stores.cs). */
export const STORE_HEADER = 'X-Yard-Store';

/**
 * Reads from one API origin, forwarding only the visitor's cookie, so a page
 * for a signed-in visitor is read as that visitor and nothing else they sent
 * goes on. Every read carries the self mark on its agent, so the Site activity
 * card does not count the page twice: the browser's own reads after it are the visit.
 * Since one web app serves both sites, every read also names the site's store,
 * because the API's address no longer says which site it is.
 */
function readerFor(
  apiOrigin: string,
  cookie: string | null,
  read: typeof fetch,
  store: string | null
): Read {
  return async <T>(path: string, signal: AbortSignal) => {
    const headers: Record<string, string> = { 'User-Agent': SELF_READ };
    if (cookie) headers.Cookie = cookie;
    if (store) headers[STORE_HEADER] = store;
    const response = await read(`${apiOrigin}${path}`, { headers, signal });
    if (!response.ok) return null;
    return (path.startsWith('/api/docs/') ? await response.text() : await response.json()) as T;
  };
}

/** A read that fails, or is cut off at the deadline, answers null, and the browser asks for it instead. */
function inTime<T>(work: Promise<T | null>): Promise<T | null> {
  return work.catch(() => null);
}
// #endregion reads

// #region loaders
/**
 * Everything an address needs drawn, read at once. The account and the build
 * go on every page (the rail and the footer show them); the list and its
 * filters on any address that opens the inventory, which a vehicle's does too,
 * because the vehicle draws over the list; the vehicle on ?vehicle=; the
 * document, rendered here with the same code the browser uses, on ?doc=.
 * Admin and the account page draw in the browser behind the frame drawn here; the review
 * route is drawn from its own list and needs no read at all; the recommendations page from its one read.
 * @param search the address's query, without its "?"
 * @param cookie the visitor's Cookie header, or null
 * @param apiOrigin the API this page is read from
 * @param deadlineMs how long the reads may take together before the page is drawn without the rest
 * @param store the site's store, named on every read, or null to take the API's default
 */
export async function loadFirst(
  search: string,
  cookie: string | null,
  apiOrigin: string,
  deadlineMs: number,
  read: typeof fetch = fetch,
  store: string | null = null
): Promise<FirstLoad> {
  const signal = AbortSignal.timeout(deadlineMs);
  const get = readerFor(apiOrigin, cookie, read, store);
  const first = readFirstAddress(search);
  const listingQuery = vehicleQueryParams(first.filters, first.sort).toString();
  const needsListing = first.inventory || first.vehicleId !== null;
  const doc = first.docKey ? DOCS[first.docKey] : null;
  // The bare address is the landing page, whose band draws the first screen's reading: read here, a crawler and a
  // link preview see the figures rather than the word the band shows before its first read.
  const landing =
    !needsListing &&
    !doc &&
    !first.admin &&
    !first.account &&
    !first.blog &&
    !first.review &&
    !first.recommendations;

  // The blog is drawn whole here, its list and the post it shows in full, so its first screen is in the page itself.
  // A post the address names is read beside the list; only the list's lead post has to wait for the list.
  const blogFirst = async (): Promise<FirstLoad['blog']> => {
    const postOf = (slug: string) =>
      inTime(get<BlogPost & { body?: string }>(fetchedFor(slug), signal));
    const named = first.blogPost ? postOf(first.blogPost) : null;
    const list = await inTime(get<BlogList>(listPath(filterFromSearch(search)), signal));
    const lead = list?.cards[0]?.slug ?? null;
    const post = named ? await named : lead ? await postOf(lead) : null;
    // The page draws the body the API drew on save; the Markdown it was drawn from rides along for the save
    // workflow's read-back and is left out here, a third of the post's bytes.
    if (post) delete post.body;
    return { list, post };
  };

  const [account, build, page, facets, vehicle, markdown, pulse, recommendations, blog] =
    await Promise.all([
      // Nobody signed in sends no cookie, and needs no read to say so.
      cookie ? inTime(get<AccountWire>('/api/auth/me', signal)) : Promise.resolve(null),
      inTime(get<{ version: string; commit: string }>('/api/version', signal)),
      needsListing
        ? inTime(get<VehiclePage>(`/api/vehicles${listingQuery ? `?${listingQuery}` : ''}`, signal))
        : Promise.resolve(null),
      needsListing ? inTime(get<InventoryFacets>('/api/facets', signal)) : Promise.resolve(null),
      first.vehicleId
        ? inTime(get<Vehicle>(`/api/vehicles/${encodeURIComponent(first.vehicleId)}`, signal))
        : Promise.resolve(null),
      doc ? inTime(get<string>(doc.url, signal)) : Promise.resolve(null),
      landing ? inTime(get<unknown>(PULSE_URL, signal)) : Promise.resolve(null),
      // The recommendations page is drawn whole from the answer, so a crawler reads every name and the count.
      first.recommendations
        ? inTime(get<unknown>(RECOMMENDATIONS_URL, signal))
        : Promise.resolve(null),
      first.blog ? blogFirst() : Promise.resolve(undefined),
    ]);

  const signedIn: Account = account ? toAccount(account) : SIGNED_OUT;
  return {
    search,
    nowMs: Date.now(),
    account: signedIn,
    build: build ? { version: String(build.version), commit: String(build.commit) } : null,
    listing: page && facets ? { page, facets } : null,
    vehicle,
    doc: first.docKey && markdown ? await renderedDoc(first.docKey, markdown) : null,
    pulse,
    recommendations,
    ...(blog ? { blog } : {}),
  };
}

/** A document as its window shows it: rendered, then laid out, the Author page in its own shape (DocDialog.tsx). */
async function renderedDoc(key: string, markdown: string): Promise<FirstLoad['doc']> {
  const rendered = await renderDocument(markdown);
  if (key !== 'author') return { key, html: layoutDocument(rendered) };
  const { layoutAuthor } = await import('../src/lib/author');
  return { key, html: layoutAuthor(rendered) };
}
// #endregion loaders
