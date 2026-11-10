/**
 * Does:      The folder entry for KeptLogsCard: re-exports KeptLogsCard, so an import names the folder,
 *            components/admin/KeptLogsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads KeptLogsCard.tsx itself, on demand.
 */
export { default } from './KeptLogsCard';
