/**
 * Does:      The folder entry for HealthCard: re-exports HealthCard, so an import names the folder,
 *            components/admin/HealthCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads HealthCard.tsx itself, on demand.
 */
export { default } from './HealthCard';
