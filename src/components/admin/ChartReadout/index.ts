/**
 * Does:      The chart readout's folder entry: the machine chart draws it from here, and the charts' short list
 *            re-exports it with the distance it sits below the plot.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   MachineChart.tsx, charts.tsx.
 */
export { ChartReadout, READOUT_DROP } from './ChartReadout';
