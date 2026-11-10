/**
 * Does:      The folder entry for FunnelCard: re-exports FunnelCard, so an import names the folder,
 *            components/admin/FunnelCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads FunnelCard.tsx itself, on demand.
 */
export { default } from './FunnelCard';
