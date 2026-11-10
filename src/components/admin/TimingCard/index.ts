/**
 * Does:      The folder entry for TimingCard: re-exports TimingCard, so an import names the folder,
 *            components/admin/TimingCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads TimingCard.tsx itself, on demand.
 */
export { default } from './TimingCard';
