/**
 * Does:      The folder entry for ErrorsCard: re-exports ErrorsCard, so an import names the folder,
 *            components/admin/ErrorsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ErrorsCard.tsx itself, on demand.
 */
export { default } from './ErrorsCard';
