/**
 * Does:      The folder entry for AdminBenchCard: re-exports AdminBenchCard, cardChunks, so an import names the
 *            folder, components/admin/AdminBenchCard.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   AdminPanel.tsx.
 */
// The part of the Admin workbench that draws one card, and the list of card chunks, re-exported
// so an import reads admin/AdminBenchCard: the card in AdminBenchCard.tsx, the chunks in cardChunks.ts.
export * from './AdminBenchCard';
export * from './cardChunks';
