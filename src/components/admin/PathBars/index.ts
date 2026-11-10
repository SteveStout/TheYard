/**
 * Does:      The front door of the PathBars folder: re-exports the component so an import reads
 *            components/admin/PathBars, the way every component folder in the repository does.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityDoors.tsx, ActivityPathTiles.tsx, ReviewCard.tsx.
 */
export * from './PathBars';
