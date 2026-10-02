/**
 * The callout on a machine chart's peak: a dot on the highest reading, a gold
 * leader line and a short label. Every position comes ready from calloutFor in
 * src/lib/machineChart.ts, so this file only draws. It is its own component so
 * the machine chart reads as the plot and its lines, with the trim beside it.
 */
import type { calloutFor } from '../../../lib/machineChart';
import styles from '../charts/charts.module.css';

/** Draws the peak callout: a dot, a leader line and a label, all placed by calloutFor. */
export function PeakCallout({
  drawn,
  testId,
}: {
  drawn: NonNullable<ReturnType<typeof calloutFor>>;
  testId: string;
}) {
  return (
    <g className={styles.callout} data-testid={`${testId}-callout`}>
      <polyline className={styles.calloutLine} points={drawn.leader} />
      <circle className={styles.calloutDot} cx={drawn.dot.x} cy={drawn.dot.y} r={3} />
      <text
        className={styles.calloutText}
        x={drawn.text.x}
        y={drawn.text.y}
        textAnchor={drawn.text.anchor}
      >
        {drawn.label}
      </text>
    </g>
  );
}
