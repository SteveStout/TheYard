/**
 * Does:      The folder entry for TelemetryCard: re-exports TelemetryCard, so an import names the folder,
 *            components/admin/TelemetryCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads TelemetryCard.tsx itself, on demand.
 */
export { default } from './TelemetryCard';
