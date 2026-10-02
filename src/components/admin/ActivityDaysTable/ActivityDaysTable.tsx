/**
 * The "Day by day" table folded under the activity chart: one row per day with its
 * visitor-days of each kind and all of them, the chart's numbers as text. It is its own
 * component because it reads only the days and has no part in drawing the chart.
 */
import {
  ACTIVITY_KINDS,
  KIND_NAMES,
  countFor,
  labelFor,
  type ActivityDay,
} from '../../../lib/activity';
import cardStyles from '../shared/card.module.css';
import { type Column, DataTable } from '../DataTable';

/** A day: its visitor-days of each kind, and all of them. */
const DAY_COLUMNS: Column<ActivityDay>[] = [
  { name: 'Day', rowHeader: true, cell: (day) => labelFor(day.day, '30d') },
  ...ACTIVITY_KINDS.map((kind): Column<ActivityDay> => ({
    name: KIND_NAMES[kind],
    mono: true,
    num: true,
    cell: (day) => day[kind].toLocaleString(),
  })),
  {
    name: 'All',
    mono: true,
    num: true,
    cell: (day) => countFor(day, 'all').toLocaleString(),
  },
];

/** The days of the window as a table inside a closed "Day by day" fold. */
export function ActivityDaysTable({ days }: { days: ActivityDay[] }) {
  return (
    <details className={cardStyles.about}>
      <summary className={cardStyles.aboutSummary}>Day by day</summary>
      <DataTable
        label="Visitor-days per day, by kind"
        testId="activity-days-table"
        rows={days}
        rowKey={(day) => day.day}
        columns={DAY_COLUMNS}
      />
    </details>
  );
}
