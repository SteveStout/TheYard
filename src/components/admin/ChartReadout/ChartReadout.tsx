/**
 * The readout a chart shows under a pointer or a finger: a rule at the slot,
 * the slot's time and each line's value. It draws inside the chart's svg and
 * takes its words from src/lib/machineChart.ts. Its own component because the
 * machine chart is the drawing and this is the one thing that answers a hover.
 */
import { MACHINE_CHART, readoutLine } from '../../../lib/machineChart';
import type { PlotBox } from '../../../lib/plotFrame';
import styles from '../charts/charts.module.css';

// #region chart-readout
/** How far the readout box sits below the plot's top, so it never covers the axis unit. */
export const READOUT_DROP = 14;

/**
 * What a chart shows under a pointer or a finger (ADR: The glass look): a
 * vertical rule at that minute, the minute's time, and each line's value in the
 * axis unit. A minute nobody measured says so, because a gap is not a zero. The
 * box sits to the right of the rule, or to the left when it would run off the
 * drawing. It is aria-hidden and takes no pointer events, so it never steals
 * the hover that shows it.
 */
export function ChartReadout({
  testId,
  x,
  when,
  rows,
  unit,
  box = MACHINE_CHART,
}: {
  testId: string;
  x: number;
  when: string;
  rows: { name: string; value: number | null }[];
  unit?: string;
  box?: PlotBox;
}) {
  const lines = [when, ...rows.map((row) => readoutLine(row.name, row.value, unit))];
  // Size the box to its longest line: about 6.2 units per character plus padding.
  const width = Math.min(360, box.width, Math.max(...lines.map((line) => line.length)) * 6.2 + 16);
  const height = lines.length * 14 + 10;
  // Beside the rule, on the side with room, and never past either edge of a narrow drawing.
  const beside = x + 8 + width > box.width - box.right ? x - 8 - width : x + 8;
  const left = Math.max(0, Math.min(beside, box.width - width));
  return (
    <g className={styles.readout} data-testid={`${testId}-readout`} aria-hidden="true">
      <line
        className={styles.readoutRule}
        x1={x}
        y1={box.top}
        x2={x}
        y2={box.height - box.bottom}
      />
      <rect
        className={styles.readoutBox}
        x={left}
        y={box.top + READOUT_DROP}
        width={width}
        height={height}
        rx="4"
      />
      {lines.map((line, index) => (
        <text
          key={line}
          className={index === 0 ? styles.readoutWhen : styles.readoutText}
          x={left + 8}
          y={box.top + READOUT_DROP + 15 + index * 14}
        >
          {line}
        </text>
      ))}
    </g>
  );
}
// #endregion chart-readout
