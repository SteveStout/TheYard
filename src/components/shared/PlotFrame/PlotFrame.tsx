/**
 * The instrument-style frame a chart is drawn in: graduation ticks on the axes
 * and gold corner brackets, placed by src/lib/plotFrame.ts. The machine charts
 * and the activity chart both draw it, so it is a component of its own rather
 * than a piece of either.
 */
import type { plotFrame } from '../../../lib/plotFrame';
import styles from '../charts/charts.module.css';

/** Draws the instrument-style frame: axis ticks and corner brackets from src/lib/plotFrame.ts. */
export function PlotFrame({
  frame,
  testId,
}: {
  frame: ReturnType<typeof plotFrame>;
  testId?: string;
}) {
  return (
    <>
      {frame.ticks.map((tick) => (
        <line
          key={tick.key}
          className={tick.major ? `${styles.markTick} ${styles.markTickMajor}` : styles.markTick}
          x1={tick.x1}
          y1={tick.y1}
          x2={tick.x2}
          y2={tick.y2}
          data-testid={testId === undefined ? undefined : `${testId}-tick`}
        />
      ))}
      {frame.brackets.map((d) => (
        <path
          key={d}
          className={styles.plotBracket}
          d={d}
          data-testid={testId === undefined ? undefined : `${testId}-bracket`}
        />
      ))}
    </>
  );
}
