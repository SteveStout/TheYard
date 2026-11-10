/**
 * Does:      The folder entry for AzureCard: re-exports AzureCard, so an import names the folder,
 *            components/admin/AzureCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: cardChunks.ts loads AzureCard.tsx itself, on demand.
 */
export { default } from './AzureCard';
