/**
 * Does:      The folder entry for ReviewCard: re-exports ReviewCard, so an import names the folder,
 *            components/admin/ReviewCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ReviewCard.tsx itself, on demand.
 */
export { default } from './ReviewCard';
