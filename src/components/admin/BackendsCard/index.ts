/**
 * Does:      The folder entry for BackendsCard: re-exports BackendsCard, so an import names the folder,
 *            components/admin/BackendsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads BackendsCard.tsx itself, on demand.
 */
export { default } from './BackendsCard';
