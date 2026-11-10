/**
 * Does:      The folder entry for PagesCard: re-exports PagesCard, so an import names the folder,
 *            components/admin/PagesCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads PagesCard.tsx itself, on demand.
 */
export { default } from './PagesCard';
