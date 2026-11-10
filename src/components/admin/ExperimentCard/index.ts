/**
 * Does:      The folder entry for ExperimentCard: re-exports ExperimentCard, so an import names the folder,
 *            components/admin/ExperimentCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ExperimentCard.tsx itself, on demand.
 */
export { default } from './ExperimentCard';
