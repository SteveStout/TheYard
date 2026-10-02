/**
 * The Admin tab's shared chart pieces (ADR: What the machines are doing),
 * named here one line per part with the file each part lives in. The
 * arithmetic (scales, paths, ticks) lives in src/lib/machineChart.ts and
 * src/lib/plotFrame.ts, which have no React in them; the parts only draw.
 * Every card imports from '../charts', so this list is the one path they use.
 */

// Sizes a chart's drawing box to the width its svg actually gets.
export { useFittedBox } from './useFittedBox';

// The box that shows a slot's values under the pointer, and how far below the plot's top it sits.
export { ChartReadout, READOUT_DROP } from '../ChartReadout';

// One resource, one chart: up to two lines on one axis.
export { MachineChart } from '../MachineChart';

// The instrument-style frame: graduation ticks and corner brackets.
export { PlotFrame } from '../PlotFrame';

// A share of a ceiling as a horizontal meter.
export { BarGauge } from '../BarGauge';

// The sentence for a window that reaches back further than the record does.
export { startLabel, youngRecord } from './youngRecord';
