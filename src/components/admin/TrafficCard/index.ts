/**
 * Does:      The folder entry for TrafficCard: re-exports TrafficCard, so an import names the folder,
 *            components/admin/TrafficCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads TrafficCard.tsx itself, on demand.
 */
export { default } from './TrafficCard';
