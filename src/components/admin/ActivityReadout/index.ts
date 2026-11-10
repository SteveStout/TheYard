/**
 * Does:      The front door of the ActivityReadout folder: re-exports the component so an import reads
 *            components/admin/ActivityReadout, the way every component folder in the repository does.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityPlot.tsx.
 */
export * from './ActivityReadout';
