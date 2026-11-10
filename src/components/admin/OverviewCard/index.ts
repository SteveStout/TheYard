/**
 * Does:      The folder entry for OverviewCard: re-exports OverviewCard, so an import names the folder,
 *            components/admin/OverviewCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads OverviewCard.tsx itself, on demand.
 */
export { default } from './OverviewCard';
