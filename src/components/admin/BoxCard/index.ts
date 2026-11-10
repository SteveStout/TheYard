/**
 * Does:      The folder entry for BoxCard: re-exports BoxCard, so an import names the folder,
 *            components/admin/BoxCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads BoxCard.tsx itself, on demand.
 */
export { default } from './BoxCard';
