/**
 * Does:      The folder entry for FollowsCard: re-exports FollowsCard, so an import names the folder,
 *            components/admin/FollowsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads FollowsCard.tsx itself, on demand.
 */
export { default } from './FollowsCard';
