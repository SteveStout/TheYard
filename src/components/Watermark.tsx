import styles from './Watermark.module.css';

/**
 * The soft watermark behind the page (ADR: The glass look): ONE inline SVG,
 * fixed behind everything, about a tenth as strong as the ink it is drawn in:
 * dotted rows in the lighter teal and nothing else. The concentric rings came
 * out on 2026-09-21 at Steve's word, and the large lightning mark in gold on
 * 2026-09-25 ("there is a odd white box in the background on desktop", "on all
 * pages"): over the ribbon ground and through the thinner glass, its straight
 * edges and its notch read as a pale box behind every page.
 * It is a drawing and not a picture, so it costs no
 * request, and it does not move, so it costs no frame. It holds no words and
 * no image, which is why it may be faint: everything that is read is solid.
 *
 * The rows are made here and not typed out, so the drawing is
 * eleven lines of arithmetic rather than forty of coordinates.
 */
const ROWS = Array.from({ length: 20 }, (_, index) => 40 + index * 44);

export function Watermark() {
  return (
    <svg
      className={styles.watermark}
      viewBox="0 0 1040 900"
      preserveAspectRatio="xMidYMid slice"
      aria-hidden="true"
      focusable="false"
      data-testid="watermark"
    >
      <g className={styles.rows}>
        {ROWS.map((y) => (
          <path key={y} d={`M0 ${y}H1040`} strokeDasharray="1 11" />
        ))}
      </g>
    </svg>
  );
}
