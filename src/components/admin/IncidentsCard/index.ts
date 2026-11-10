/**
 * Does:      The folder entry for IncidentsCard: re-exports IncidentsCard, so an import names the folder,
 *            components/admin/IncidentsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads IncidentsCard.tsx itself, on demand.
 */
export { default } from './IncidentsCard';
