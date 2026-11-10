/**
 * Does:      The folder entry for ProofCard: re-exports ProofCard, so an import names the folder,
 *            components/admin/ProofCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ProofCard.tsx itself, on demand.
 */
export { default } from './ProofCard';
