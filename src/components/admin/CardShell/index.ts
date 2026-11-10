/**
 * Does:      The folder entry for CardShell: re-exports CardShell, so an import names the folder,
 *            components/admin/CardShell.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   BoxCard.tsx, CloudResourceCard.tsx, PlanCard.tsx.
 */
export { CardShell } from './CardShell';
