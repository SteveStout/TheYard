/**
 * Does:      The folder entry for ActivityCard: re-exports ActivityCard, so an import names the folder,
 *            components/admin/ActivityCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ActivityCard.tsx itself, on demand.
 */
export { default } from './ActivityCard';
