/**
 * Does:      The folder entry for OperatorCard: re-exports OperatorCard, so an import names the folder,
 *            components/admin/OperatorCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads OperatorCard.tsx itself, on demand.
 */
export { default } from './OperatorCard';
