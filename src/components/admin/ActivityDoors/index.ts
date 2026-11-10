/**
 * Does:      The front door of the ActivityDoors folder: re-exports the component so an import reads
 *            components/admin/ActivityDoors, the way every component folder in the repository does.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityPathTiles.tsx.
 */
export * from './ActivityDoors';
