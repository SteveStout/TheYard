/**
 * Does:      The folder entry for TestsCard: re-exports TestsCard, so an import names the folder,
 *            components/admin/TestsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads TestsCard.tsx itself, on demand.
 */
export { default } from './TestsCard';
