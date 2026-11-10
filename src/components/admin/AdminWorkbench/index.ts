/**
 * Does:      The folder entry for AdminWorkbench: re-exports AdminWorkbench, so an import names the folder,
 *            components/admin/AdminWorkbench.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   AdminPanel.tsx.
 */
// The Admin workbench's layout, re-exported so an import reads admin/AdminWorkbench.
// The component itself is in AdminWorkbench.tsx beside this file.
export * from './AdminWorkbench';
