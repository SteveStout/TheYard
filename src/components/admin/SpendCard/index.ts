/**
 * Does:      The folder entry for SpendCard: re-exports SpendCard, so an import names the folder,
 *            components/admin/SpendCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads SpendCard.tsx itself, on demand.
 */
export { default } from './SpendCard';
