/**
 * Does:      The front door of the ActivityPathTiles folder: re-exports the component so an import reads
 *            components/admin/ActivityPathTiles, the way every component folder in the repository does.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityGraph.tsx.
 */
export * from './ActivityPathTiles';
