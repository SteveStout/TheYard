/**
 * The arithmetic and the words of a chart's readout, the box that shows a
 * slot's values under a pointer or a finger (ADR: The glass look). It turns an
 * x in the drawing into a slot, a slot back into an x, and a reading into a
 * line of text. Its own file because the readout is its own job, and the
 * frame and the callout borrow its xOf.
 */

import type { PlotBox } from './plotFrame';
import { MACHINE_CHART } from './machineChartPlot';

// #region readout
/**
 * Which reading a pointer is over (ADR: The glass look, the readout on a
 * chart): the nearest slot to an x in the drawing's own units, clamped to the
 * plot, so a finger at the edge of the card reads the first or the last slot
 * and never nothing.
 */
export function nearestIndex(
  x: number,
  count: number,
  box: PlotBox = MACHINE_CHART
): number | null {
  if (count <= 0 || Number.isNaN(x)) return null;
  if (count === 1) return 0;
  const innerWidth = box.width - box.left - box.right;
  const step = innerWidth / (count - 1);
  return Math.min(count - 1, Math.max(0, Math.round((x - box.left) / step)));
}

/** Where a slot is drawn, across: the same arithmetic the line uses. */
export function xOf(index: number, count: number, box: PlotBox = MACHINE_CHART): number {
  const innerWidth = box.width - box.left - box.right;
  return box.left + (count <= 1 ? 0 : (index * innerWidth) / (count - 1));
}

/** One line of the readout: a reading in the axis's unit, or the words for a slot nobody measured. */
export function readoutLine(name: string, value: number | null, unit?: string): string {
  if (value === null) return `${name}: not measured`;
  const shown = value.toLocaleString('en-US');
  return `${name}: ${shown}${unit === undefined ? '' : unit === '%' ? '%' : ` ${unit}`}`;
}

// #endregion readout
