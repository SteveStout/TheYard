/**
 * The weekly rhythm drawn onto a daily chart: the weekends washed across the
 * plot and a label on every Monday and Friday, placed by src/lib/dayMarks.ts.
 * The activity chart and the machine charts both draw it over a week or a
 * month, so it is a component of its own rather than a piece of either; it
 * draws nothing over an hour or a day.
 */
import { dayAxisLabels, dayMarks, weekendRuns } from '../../../lib/dayMarks';
import type { PlotBox } from '../../../lib/plotFrame';
import styles from '../charts/charts.module.css';
import dayStyles from './DayMarks.module.css';

/** Draws the weekend wash and the Monday and Friday labels for `slots` across `box`. */
export function DayMarks({
  slots,
  windowMinutes,
  labelled = true,
  box,
  testId,
}: {
  /** The chart's slots, oldest first, each with its time: a day as `2026-10-12` or an ISO instant. */
  slots: { at: string }[];
  /** How long the window is; under a day nothing is drawn. */
  windowMinutes: number;
  /** Whether the Monday and Friday labels are drawn; a chart that names every day draws only the wash. */
  labelled?: boolean;
  box: PlotBox;
  testId: string;
}) {
  const marks = dayMarks(slots, windowMinutes);
  if (marks.length === 0) return null;
  const count = slots.length;
  const step = count <= 1 ? 0 : (box.width - box.left - box.right) / (count - 1);
  const xAt = (index: number) => box.left + index * step;
  const anchor = (index: number) =>
    index === 0 ? 'start' : index === count - 1 ? 'end' : 'middle';
  return (
    <>
      {weekendRuns(marks).map((run) => (
        <rect
          key={`w${run.from}`}
          className={dayStyles.weekendBand}
          x={xAt(run.from) - step / 2}
          y={box.top}
          width={step * (run.to - run.from + 1)}
          height={box.height - box.top - box.bottom}
          data-testid={`${testId}-weekend`}
        />
      ))}
      {labelled &&
        dayAxisLabels(marks, slots).map((label) => (
          <text
            key={`d${label.index}`}
            className={styles.axisLabel}
            x={xAt(label.index)}
            y={box.height - 8}
            textAnchor={anchor(label.index)}
            data-testid={`${testId}-day-label`}
          >
            {label.text}
          </text>
        ))}
    </>
  );
}
