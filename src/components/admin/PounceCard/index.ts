/**
 * Does:      The folder entry for PounceCard: re-exports PounceCard, so an import names the folder,
 *            components/admin/PounceCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads PounceCard.tsx itself, on demand.
 */
export { default } from './PounceCard';
