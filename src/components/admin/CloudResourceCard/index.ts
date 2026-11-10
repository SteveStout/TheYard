/**
 * Does:      The folder entry for CloudResourceCard: re-exports CloudResourceCard, so an import names the folder,
 *            components/admin/CloudResourceCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads CloudResourceCard.tsx itself, on demand.
 */
export { default } from './CloudResourceCard';
