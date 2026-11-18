/**
 * Does:      The folder entry for BlogFilters: re-exports BlogFilters, so an import names the folder,
 *            components/blog/BlogFilters.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   BlogPage.tsx.
 */
export { BlogFilters, type FilterChange } from './BlogFilters';
