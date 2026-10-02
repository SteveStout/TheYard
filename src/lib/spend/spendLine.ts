/**
 * The geometry of the spend line: the running total as an SVG path, the
 * forecast carried on from it as a second path, the newest day's marker, and
 * the axis. It is its own file because it is the largest of the three
 * pictures, and its scale and layout rules belong together.
 */
import type { CostReport } from './costReport';
import { dayShort, money } from './costWords';

// #region spend-line
/** The chart's size and the margins around the plot, in SVG units. */
export type LineBox = {
  width: number;
  height: number;
  left: number;
  right: number;
  top: number;
  bottom: number;
};

/** The box the cost card draws its spend line in. */
export const SPEND_BOX: LineBox = {
  width: 640,
  height: 220,
  left: 56,
  right: 16,
  top: 16,
  bottom: 28,
};

/** Everything the card needs to draw the spend line, already placed in the box. */
export type SpendLine = {
  /** The spend so far, a running total, as an SVG path. */
  actual: string;
  /** The forecast carried on from the last reported day, as an SVG path; empty when not drawn. */
  forecast: string;
  /** Where the newest day sits, for its marker; null when there are no days. */
  newest: { x: number; y: number; partial: boolean } | null;
  /** The axis: a few round amounts and where they sit. */
  ticks: { y: number; label: string }[];
  /** The first and last day under the line, and where each sits. */
  ends: { x: number; label: string }[];
  ceiling: number;
};

/** A round top for the axis: 1, 2 or 5 times a power of ten, at or above the highest reading. */
export function niceCeiling(value: number): number {
  if (value <= 0) return 1;
  const power = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 2, 5, 10]) {
    if (value <= step * power) return step * power;
  }
  return 10 * power;
}

/**
 * The spend line: each reported day's running total, and on the month window
 * the forecast carried on from it to the month's end. A day is a step on the
 * axis whether it cost anything or not; a day not yet reported is not drawn.
 */
export function spendLine(
  report: CostReport,
  withForecast: boolean,
  box: LineBox = SPEND_BOX
): SpendLine {
  const ahead = withForecast ? report.forecast : [];
  const all = [...report.days, ...ahead];
  const ceiling = niceCeiling(Math.max(0, ...all.map((point) => point.total)));
  const innerWidth = box.width - box.left - box.right;
  const innerHeight = box.height - box.top - box.bottom;
  const step = all.length <= 1 ? 0 : innerWidth / (all.length - 1);
  const x = (index: number) => box.left + (all.length <= 1 ? innerWidth / 2 : index * step);
  const y = (value: number) => box.top + innerHeight - (value / ceiling) * innerHeight;
  const path = (points: { x: number; y: number }[]) =>
    points
      .map(
        (point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(1)},${point.y.toFixed(1)}`
      )
      .join(' ');

  const actualPoints = report.days.map((point, index) => ({ x: x(index), y: y(point.total) }));
  const last = report.days.length - 1;
  // The forecast starts where the reported line ends, so the two read as one line that turns to dashes.
  const forecastPoints =
    ahead.length === 0 || last < 0
      ? []
      : [
          actualPoints[last],
          ...ahead.map((point, index) => ({ x: x(last + 1 + index), y: y(point.total) })),
        ];
  const ticks = [0, 0.5, 1].map((share) => ({
    y: y(ceiling * share),
    label: money(ceiling * share, report.currency),
  }));
  const ends =
    all.length === 0
      ? []
      : all.length === 1
        ? [{ x: x(0), label: dayShort(all[0].day) }]
        : [
            { x: x(0), label: dayShort(all[0].day) },
            { x: x(all.length - 1), label: dayShort(all[all.length - 1].day) },
          ];
  return {
    actual: path(actualPoints),
    forecast: path(forecastPoints),
    newest:
      last < 0
        ? null
        : { x: actualPoints[last].x, y: actualPoints[last].y, partial: report.days[last].partial },
    ticks,
    ends,
    ceiling,
  };
}
// #endregion spend-line
