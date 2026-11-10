/**
 * Does:      The folder entry for SqlCard: re-exports SqlCard, so an import names the folder,
 *            components/admin/SqlCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads SqlCard.tsx itself, on demand.
 */
export { default } from './SqlCard';
