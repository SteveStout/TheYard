/**
 * Does:      The folder entry for PlanCard: re-exports PlanCard, so an import names the folder,
 *            components/admin/PlanCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: BoxCard.tsx and CloudResourceCard.tsx import PlanCard.tsx itself, and cardChunks.ts loads it on
 *            demand.
 */
export { default } from './PlanCard';
