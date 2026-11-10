/**
 * Does:      The folder entry for ReviewStop: re-exports ReviewStop, so an import names the folder,
 *            components/review/ReviewStop.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ReviewPage.tsx.
 */
export { ReviewStop, type StopState } from './ReviewStop';
