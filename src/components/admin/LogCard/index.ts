/**
 * Does:      The folder entry for LogCard: re-exports LogCard, so an import names the folder,
 *            components/admin/LogCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads LogCard.tsx itself, on demand.
 */
export { default } from './LogCard';
