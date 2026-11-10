/**
 * Does:      The folder entry for ResetLinkCard: re-exports ResetLinkCard, so an import names the folder,
 *            components/admin/ResetLinkCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ResetLinkCard.tsx itself, on demand.
 */
export { default } from './ResetLinkCard';
