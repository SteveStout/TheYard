/**
 * The arithmetic behind the machine charts (ADR: What the machines are doing,
 * the addendum on drawing them), named here part by part with the file each
 * part lives in. Plain functions over plain data, no React, so the drawing is
 * a few paths and the reasoning is testable on its own. Every importer reads
 * from this one path; the parts beside it each hold one job.
 */

// The plot: its size, the top of its axis, the line and the labels along the bottom.
export type { ChartPoint, ChartSeries } from './machineChartPlot';
export { MACHINE_CHART, ceilingFor, pathFor, ticks, clockLabel } from './machineChartPlot';

// The readout under a pointer: which slot it is over, where a slot is drawn, and its words.
export { nearestIndex, xOf, readoutLine } from './machineChartReadout';

// The instrument-style frame and the callout on a series' peak.
export { machineFrame, peakOf, calloutFor } from './machineChartFrame';

// The four windows, and a kept window's buckets as a timeline with its gaps.
export type { MachineWindow, KeptBucket } from './machineWindows';
export {
  MACHINE_WINDOWS,
  windowName,
  timeline,
  shareOf,
  busiestRate,
  requestUnitsAMinute,
  axisLabel,
  LEAST_SLOTS,
  fromFirstReading,
  coverage,
} from './machineWindows';

// Traffic as slots whatever the window, its totals, and the proof's paired bars.
export type { TrafficMinute, TrafficSlot, KeptTotals, PairedBar } from './trafficSlots';
export {
  hourOfTraffic,
  keptTraffic,
  keptSparks,
  trafficTotals,
  keptWindowTotals,
  pairedBars,
} from './trafficSlots';
