/**
 * Does:      The folder entry for SectionHead: re-exports SectionHead, so an import names the folder,
 *            components/shared/SectionHead.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ReviewPath.tsx, ReviewTour.tsx, ReviewVoices.tsx, ReviewWork.tsx, ReviewPage.tsx.
 */
export { SectionHead } from './SectionHead';
