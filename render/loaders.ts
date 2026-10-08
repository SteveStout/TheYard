/**
 * Does:      Reads from the API what an address needs drawn before it is drawn: who is signed in, the build, the first page
 *            of the list, the vehicle, the document. One loader per view, all at once, each under one deadline.
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
import { SELF_READ } from './page';

// #region reads
/** One read of the API: its JSON, or null on anything but a 200. */
type Read = <T>(path: string, signal: AbortSignal) => Promise<T | null>;

/**
 * Reads from one API origin, forwarding only the visitor's cookie, so a page
 * for a signed-in visitor is read as that visitor and nothing else they sent
 * goes on. Every read carries the self mark on its agent, so the Site activity
 * card does not count the page twice: the browser's own reads after it are the visit.
 */
function readerFor(apiOrigin: string, cookie: string | null, read: typeof fetch): Read {
  return async <T>(path: string, signal: AbortSignal) => {
    const headers: Record<string, string> = { 'User-Agent': SELF_READ };
    if (cookie) headers.Cookie = cookie;
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
 * Admin and the account page draw in the browser behind the frame drawn here.
 * @param search the address's query, without its "?"
 * @param cookie the visitor's Cookie header, or null
 * @param apiOrigin the API this page is read from
 * @param deadlineMs how long the reads may take together before the page is drawn without the rest
 */
export async function loadFirst(
  search: string,
  cookie: string | null,
  apiOrigin: string,
  deadlineMs: number,
  read: typeof fetch = fetch
): Promise<FirstLoad> {
  const signal = AbortSignal.timeout(deadlineMs);
  const get = readerFor(apiOrigin, cookie, read);
  const first = readFirstAddress(search);
  const listingQuery = vehicleQueryParams(first.filters, first.sort).toString();
  const needsListing = first.inventory || first.vehicleId !== null;
  const doc = first.docKey ? DOCS[first.docKey] : null;

  const [account, build, page, facets, vehicle, markdown] = await Promise.all([
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
