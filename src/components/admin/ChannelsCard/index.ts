/**
 * Does:      The folder entry for ChannelsCard: re-exports ChannelsCard, so an import names the folder,
 *            components/admin/ChannelsCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads ChannelsCard.tsx itself, on demand.
 */
export { default } from './ChannelsCard';
