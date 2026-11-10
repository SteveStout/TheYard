/**
 * Does:      The folder entry for BlogCard: re-exports BlogCard, so an import names the folder,
 *            components/admin/BlogCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads BlogCard.tsx itself, on demand.
 */
export { default } from './BlogCard';
