/**
 * Does:      The folder entry for AdminHourAtAGlance: re-exports AdminHourAtAGlance, so an import names the folder,
 *            components/admin/AdminHourAtAGlance.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   AdminWorkbench.tsx.
 */
// The Admin workbench's "This hour" panel, re-exported so an import reads admin/AdminHourAtAGlance.
// The component itself is in AdminHourAtAGlance.tsx beside this file.
export * from './AdminHourAtAGlance';
