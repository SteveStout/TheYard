/**
 * The readout box beside the activity chart's crosshair: the day, its total, and one
 * swatched line per band or store. It sits on the side of the chart away from the
 * pointer. It is its own component so the plot file holds the drawing and this the words.
 */
import { labelFor } from '../../../lib/activity';
import styles from '../ActivityCard/ActivityCard.module.css';
import chartStyles from '../charts/charts.module.css';

/** One line of the readout: its key, colour class, name, its own value, and its top on the axis. */
export type ActivityReading = {
  key: string;
  className: string;
  name: string;
  value: number;
  top: number;
};

/**
 * Shows the hovered day's readings. The today note is set only when the day is today and
 * still under way; onLeft puts the box on the left when the crosshair is right of centre.
 */
export function ActivityReadout({
  day,
  todayNote,
  readings,
  total,
  onLeft,
}: {
  day: string;
  todayNote: string | null;
  readings: ActivityReading[];
  total: number;
  onLeft: boolean;
}) {
  return (
    <div
      className={`${styles.chartTip} ${onLeft ? styles.chartTipLeft : styles.chartTipRight}`}
      role="status"
      data-testid="activity-tooltip"
    >
      <strong>
        {labelFor(day, '30d')}
        {todayNote !== null ? `, ${todayNote}` : ''}: {total.toLocaleString()}
      </strong>
      {readings.map((reading) => (
        <span key={reading.key}>
          <span className={`${chartStyles.swatch} ${reading.className}`} aria-hidden="true" />
          {reading.name} {reading.value.toLocaleString()}
        </span>
      ))}
    </div>
  );
}
