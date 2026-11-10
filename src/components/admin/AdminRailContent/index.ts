/**
 * Does:      The folder entry for AdminRailContent: re-exports AdminRailContent, so an import names the folder,
 *            components/admin/AdminRailContent.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   AdminWorkbench.tsx.
 */
// The Admin workbench's rail of cards, re-exported so an import reads admin/AdminRailContent.
// The component itself is in AdminRailContent.tsx beside this file.
export * from './AdminRailContent';
