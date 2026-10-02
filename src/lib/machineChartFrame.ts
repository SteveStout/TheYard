/**
 * The instrument-style frame of a machine chart and the callout on its peak
 * (ADR: The tweaks pass): where the graduation ticks go along the bottom, where
 * a series' highest reading is drawn, and how the label pointing at it is
 * placed. Its own file because the frame and the callout are the chart's
 * trim, decided apart from the line and the readings under it.
 */

import { plotFrame, type PlotBox } from './plotFrame';
import { type ChartPoint, type ChartSeries, MACHINE_CHART, ticks } from './machineChartPlot';
import { xOf } from './machineChartReadout';
import { axisLabel, type MachineWindow } from './machineWindows';

// #region mark-vii
/**
 * The Mark VII frame for a machine chart of `count` slots (ADR: The tweaks
 * pass), drawn in place of a grid: the labelled slots as major ticks along the
 * bottom, eight even steps as minor ones, and the side and brackets every
 * framed chart shares (src/lib/plotFrame.ts).
 */
export function machineFrame(count: number, box: PlotBox = MACHINE_CHART) {
  const inner = box.width - box.left - box.right;
  const minor = Array.from({ length: 9 }, (_, step) => ({
    key: `m${step}`,
    x: box.left + (inner * step) / 8,
    major: false,
  }));
  const major = ticks(count).map((index) => ({
    key: `x${index}`,
    x: xOf(index, count, box),
    major: true,
  }));
  return plotFrame(box, [...minor, ...major]);
}

/** A series' highest reading and where it is drawn, for the callout; null when nothing rose above zero. */
export function peakOf(
  points: ChartPoint[],
  ceiling: number,
  box: PlotBox = MACHINE_CHART
): { index: number; value: number; x: number; y: number } | null {
  let index = -1;
  let value = 0;
  points.forEach((point, at) => {
    if (point.value !== null && point.value > value) {
      value = point.value;
      index = at;
    }
  });
  if (index < 0 || ceiling <= 0) return null;
  const innerHeight = box.height - box.top - box.bottom;
  const y = box.top + innerHeight - (Math.min(value, ceiling) / ceiling) * innerHeight;
  return {
    index,
    value,
    x: Math.round(xOf(index, points.length, box) * 10) / 10,
    y: Math.round(y * 10) / 10,
  };
}

/** The callout's leader: out from the peak, down from the top of the plot, then a shelf the label sits on. */
const CALLOUT = { reach: 14, drop: 10, shelf: 8 } as const;

/**
 * The peak callout (ADR: The tweaks pass), placed: the dot on the peak, a
 * leader up to an elbow under the top of the plot, and the label running away
 * from the nearer edge. Null when the series is not there or never rose above
 * zero.
 */
export function calloutFor(
  series: ChartSeries[],
  callout: { key: string; name: string },
  ceiling: number,
  window: MachineWindow,
  box: PlotBox = MACHINE_CHART
): {
  label: string;
  dot: { x: number; y: number };
  leader: string;
  text: { x: number; y: number; anchor: 'start' | 'end' };
} | null {
  const line = series.find((one) => one.key === callout.key);
  const peak = line === undefined ? null : peakOf(line.points, ceiling, box);
  if (line === undefined || peak === null) return null;
  const toRight = peak.x < box.width / 2;
  const away = toRight ? 1 : -1;
  const elbow = { x: peak.x + away * CALLOUT.reach, y: box.top + CALLOUT.drop };
  return {
    label: `${callout.name} · peak ${peak.value.toLocaleString()} at ${axisLabel(line.points[peak.index].at, window)}`,
    dot: { x: peak.x, y: peak.y },
    leader: `${peak.x},${peak.y} ${elbow.x},${elbow.y} ${elbow.x + away * CALLOUT.shelf},${elbow.y}`,
    text: {
      x: elbow.x + away * (CALLOUT.shelf + 3),
      y: elbow.y + 3,
      anchor: toRight ? 'start' : 'end',
    },
  };
}

// #endregion mark-vii
