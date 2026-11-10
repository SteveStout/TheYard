/**
 * Does:      The folder entry for LinksCard: re-exports LinksCard, so an import names the folder,
 *            components/admin/LinksCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads LinksCard.tsx itself, on demand.
 */
export { default } from './LinksCard';
