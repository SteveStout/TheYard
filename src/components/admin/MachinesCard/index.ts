/**
 * Does:      The folder entry for MachinesCard: re-exports MachinesCard, so an import names the folder,
 *            components/admin/MachinesCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads MachinesCard.tsx itself, on demand.
 */
export { default } from './MachinesCard';
