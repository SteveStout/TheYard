/**
 * Does:      The folder entry for WorkingLock: re-exports WorkingLock, so an import names the folder,
 *            components/shared/WorkingLock.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   TheYard.tsx, useLiveBlocks.tsx.
 */
export * from './WorkingLock';
