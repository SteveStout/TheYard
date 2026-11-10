/**
 * Does:      The folder entry for DayMarks: re-exports DayMarks, so an import names the folder,
 *            components/admin/DayMarks.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ActivityPlot.tsx, MachineChart.tsx, PeopleReadouts.tsx.
 */
export { DayMarks } from './DayMarks';
