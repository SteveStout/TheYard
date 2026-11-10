/**
 * Does:      The folder entry for AdminPanel: re-exports AdminPanel, so an import names the folder,
 *            components/admin/AdminPanel.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   nothing: App.tsx loads AdminPanel.tsx itself, on demand.
 */
export * from './AdminPanel';
