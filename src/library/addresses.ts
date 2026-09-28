/**
 * Does:      Turns a document into its address (?doc=slug) and an address back into a document.
 * Does not:  Read or write the address bar (useAddressBar.ts does), or fetch a document.
 * Used by:   useAddressBar.ts, useNavigation.ts.
 */
import { DOCS, type DocKey } from './documents';

// #region doc-addresses
/**
 * The slug in a document's API URL, which is also the name it takes in the
 * address bar. Derived from the one URL rather than written a second time, so
 * a record cannot become linkable under a name the API does not serve.
 */
export function docSlug(key: DocKey): string {
  return DOCS[key].url.slice('/api/docs/'.length);
}

const KEY_BY_SLUG: Record<string, DocKey> = Object.fromEntries(
  (Object.keys(DOCS) as DocKey[]).map((key) => [docSlug(key), key])
);

/**
 * The document a `?doc=` value names, or null when it names none. An address
 * that matches nothing opens nothing: a stale link should land on the
 * inventory, not on an error.
 */
export function docKeyForSlug(slug: string | null): DocKey | null {
  return slug === null ? null : (KEY_BY_SLUG[slug] ?? null);
}
// #endregion doc-addresses
