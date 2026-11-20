/**
 * Does:      Draws the small PDF page that stands before the words on the resume button, as the quality1120 picture
 *            draws it: a white page with a folded corner, edged in the gold bar's dark tone, and a deep teal band
 *            reading PDF.
 * Does not:  Draw the button (panels.css's .op-pill-resume does) or say anything to a screen reader: the button's
 *            words do.
 * Used by:   ReviewVerdict.tsx, ReviewMirror.tsx, ReviewClose.tsx, ReviewDock.tsx, Landing.tsx.
 */
import styles from './PdfMark.module.css';

export function PdfMark() {
  return (
    <svg
      className={styles.mark}
      viewBox="0 0 20 24"
      width="20"
      height="24"
      aria-hidden="true"
      focusable="false"
    >
      <path
        className={styles.page}
        d="M2 1h11l5 5v16a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1z"
      />
      <path className={styles.fold} d="M13 1v5h5" />
      <rect className={styles.band} x="3" y="13" width="14" height="6" rx="1" />
      <text className={styles.word} x="10" y="17.8" fontSize="5" textAnchor="middle">
        PDF
      </text>
    </svg>
  );
}
