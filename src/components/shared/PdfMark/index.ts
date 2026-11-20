/**
 * Does:      The folder entry for PdfMark: re-exports PdfMark, so an import names the folder, components/shared/PdfMark.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ReviewVerdict.tsx, ReviewMirror.tsx, ReviewClose.tsx, ReviewDock.tsx, Landing.tsx, AskClose.tsx.
 */
export { PdfMark } from './PdfMark';
