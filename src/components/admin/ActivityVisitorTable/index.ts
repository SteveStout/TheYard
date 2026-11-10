/**
 * Does:      The front door of the ActivityVisitorTable folder: re-exports the component so an import reads
 *            components/admin/ActivityVisitorTable, the way every component folder in the repository does.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityCard.tsx.
 */
export * from './ActivityVisitorTable';
