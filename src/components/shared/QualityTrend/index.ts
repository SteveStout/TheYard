/**
 * Does:      The quality trend's folder entry: the Quality page's live blocks draw each number's line from here, and the
 *            landing's hook draws the Quality page's first chart (ADR-132).
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   useLiveBlocks.tsx, Landing.tsx.
 */
export { QualityTrend } from './QualityTrend';
