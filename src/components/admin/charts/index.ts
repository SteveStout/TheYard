/**
 * Does:      The folder entry for charts: re-exports charts, so an import names the folder, components/admin/charts.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityPlot.tsx, MachinesContainerSection.tsx, MachinesDocumentSection.tsx, MachinesKeptWindow.tsx,
 *            MachinesRelationalSection.tsx, PathBars.tsx, SpendCard.tsx, TrafficCard.tsx, charts.tsx.
 */
export * from './charts';
