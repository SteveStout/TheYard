/**
 * The head of the Site activity card: people who have visited, counted once, all time, in four
 * readouts, and the thirty days of new people drawn over the visitor-days of people the card
 * already counts (ADR: People who have visited, counted once). Every readout names where it is
 * read from. Before the count has begun, the figures say so in words and the chart is empty,
 * the way every other readout on the tab says a figure it could not read.
 */
import type { ActivityDay } from '../../../lib/activity';
import {
  countedFromWords,
  countWords,
  monthWords,
  type PeopleCount,
  type PeopleDay,
} from '../../../lib/people';
import { DayMarks } from '../DayMarks';
import chartStyles from '../charts/charts.module.css';
import styles from './PeopleReadouts.module.css';

/** The chart's box, in viewBox units: room under the plot for the day labels, left of it for the scale. */
const BOX = { width: 640, height: 170, top: 10, right: 8, bottom: 28, left: 32 };

/** The scale's top: the tallest day of either line, rounded up to a tidy step, never below ten. */
function ceiling(values: number[]): number {
  const top = Math.max(10, ...values);
  const step = top <= 50 ? 10 : top <= 200 ? 50 : 100;
  return Math.ceil(top / step) * step;
}

/** A line through the days, oldest first: x by the day's place, y by its value against the scale's top. */
function points(values: (number | null)[], top: number): string {
  const step = values.length <= 1 ? 0 : (BOX.width - BOX.left - BOX.right) / (values.length - 1);
  const tall = BOX.height - BOX.top - BOX.bottom;
  return values
    .map((value, index) =>
      value === null
        ? null
        : `${(BOX.left + index * step).toFixed(1)},${(BOX.height - BOX.bottom - (tall * value) / top).toFixed(1)}`
    )
    .filter((point): point is string => point !== null)
    .join(' ');
}

export function PeopleReadouts({
  count,
  days,
}: {
  /** The count: null while it is read, 'failed' when the read failed. */
  count: PeopleCount | null | 'failed';
  /** The activity report's days, for the visitor-days of people under the new-people line. */
  days: ActivityDay[];
}) {
  const read = count === null || count === 'failed' ? null : count;
  const source = read?.source ?? 'Cosmos DB visitors';
  const thirty: PeopleDay[] = read?.people_new_by_day ?? [];
  const visitorDays = new Map(days.map((day) => [day.day, day.people]));
  const everybody = thirty.map((day) => visitorDays.get(day.day) ?? null);
  const top = ceiling([
    ...thirty.map((day) => day.people),
    ...everybody.filter((value): value is number => value !== null),
  ]);
  const unread =
    count === 'failed' ? 'could not be read' : count === null ? 'reading' : 'not counted yet';
  const figure = (value: number | null | undefined, testId: string, unit?: string) =>
    read === null || value === null || value === undefined ? (
      <p className={styles.figure}>
        <span className={styles.unread} data-testid={testId}>
          {read === null ? unread : `counting from ${countedFromWords(read.counted_from)}`}
        </span>
      </p>
    ) : (
      <p className={styles.figure}>
        <span data-testid={testId}>{countWords(value)}</span>
        {unit !== undefined && <span className={styles.unit}>{unit}</span>}
      </p>
    );

  return (
    <section aria-label="People who have visited" data-testid="people-readouts">
      <div className={styles.head}>
        <div className={`${styles.readout} ${styles.lead}`}>
          <p className={styles.label}>
            <span className={styles.swatch} aria-hidden="true" />
            People who have visited
          </p>
          {figure(read?.people_all_time, 'people-all-time')}
          <p className={styles.sub}>
            all time
            {read === null ? '' : ` · counted from ${countedFromWords(read.counted_from)}`} ·
            source: {source}
          </p>
        </div>
        <div className={styles.readout}>
          <p className={styles.label}>New today</p>
          {figure(read?.people_new_today, 'people-new-today')}
          <p className={styles.sub}>first seen today, UTC · source: {source}</p>
        </div>
        <div className={styles.readout}>
          <p className={styles.label}>New this month</p>
          {figure(read?.people_new_month, 'people-new-month')}
          <p className={styles.sub}>
            first seen in{' '}
            {monthWords(
              new Date(`${thirty.at(-1)?.day ?? read?.counted_from ?? '2026-10-23'}T00:00:00Z`)
            )}{' '}
            · source: {source}
          </p>
        </div>
        <div className={styles.readout}>
          <p className={styles.label}>Left out of the count</p>
          {figure(read?.scanners_left_out, 'people-left-out', 'scanners')}
          <p className={styles.sub}>plus my own reads and the gate&apos;s · source: {source}</p>
        </div>
      </div>
      <h3 className={styles.chartHead}>New people each day</h3>
      <svg
        className={styles.chart}
        viewBox={`0 0 ${BOX.width} ${BOX.height}`}
        role="img"
        aria-label={
          thirty.length === 0
            ? 'New people each day: nothing counted yet'
            : `New people each day for the last ${thirty.length} days, on a scale to ${top}`
        }
        data-testid="people-chart"
      >
        <DayMarks
          slots={thirty.map((day) => ({ at: day.day }))}
          windowMinutes={30 * 24 * 60}
          box={BOX}
          testId="people-chart"
        />
        {[0, 0.5, 1].map((share) => {
          const y = BOX.height - BOX.bottom - (BOX.height - BOX.top - BOX.bottom) * share;
          return (
            <g key={share}>
              <line
                className={chartStyles.axis}
                x1={BOX.left}
                x2={BOX.width - BOX.right}
                y1={y}
                y2={y}
              />
              <text x={BOX.left - 6} y={y + 4} textAnchor="end" fontSize="11">
                {Math.round(top * share)}
              </text>
            </g>
          );
        })}
        {everybody.some((value) => value !== null) && (
          <polyline
            className={`${chartStyles.line} ${chartStyles.thirdLine}`}
            points={points(everybody, top)}
          />
        )}
        {thirty.length > 0 && (
          <polyline
            className={`${chartStyles.line} ${styles.people}`}
            points={points(
              thirty.map((day) => day.people),
              top
            )}
            data-testid="people-chart-line"
          />
        )}
      </svg>
      <ul className={styles.legend}>
        <li className={styles.legendPeople}>people, first seen that day</li>
        <li className={chartStyles.thirdLine}>
          everybody who came that day, as people (the visitor-days the card draws below)
        </li>
      </ul>
    </section>
  );
}
