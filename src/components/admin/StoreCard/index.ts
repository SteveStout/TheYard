/**
 * Does:      The folder entry for StoreCard: re-exports StoreCard, so an import names the folder,
 *            components/admin/StoreCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads StoreCard.tsx itself, on demand.
 */
export { default } from './StoreCard';
