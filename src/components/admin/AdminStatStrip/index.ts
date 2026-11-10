/**
 * Does:      The folder entry for AdminStatStrip: re-exports AdminStatStrip, so an import names the folder,
 *            components/admin/AdminStatStrip.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   AdminPanel.tsx.
 */
// The Admin tab's strip of tiles, re-exported so an import reads admin/AdminStatStrip.
// The component itself is in AdminStatStrip.tsx beside this file.
export * from './AdminStatStrip';
