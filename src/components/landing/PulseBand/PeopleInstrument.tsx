/**
 * The band's fourth instrument: the people who have visited, as the whole kept history of visits
 * when the server has counted it (one a person a day since the first day kept), and as the people
 * counted once since the count began until then, with the last thirty days drawn under the figure
 * (ADR: People who have visited, counted once, and its addendum on the kept visits). Its own file
 * because it is the one instrument with a chart and three states, and the band's file is the four
 * boxes. The band hands it the pulse it read; it reads nothing itself.
 */
import { barHeights, dayWords, type Pulse, unitOf, UNREAD } from '../../../lib/pulse';
import type { PulseDoc } from './PulseBand';
import styles from './PulseBand.module.css';

export function PeopleInstrument({
  pulse,
  onOpenDoc,
}: {
  /** The reading the band holds, or null before the first arrives. */
  pulse: Pulse | null;
  /** Opens the record that says how a visitor is counted. */
  onOpenDoc: (key: PulseDoc) => void;
}) {
  const people = pulse?.people_all_time ?? null;
  // The whole kept history as visits when the server has counted it; the people counted once
  // since the count began until then. The bars under the figure draw the same thing it counts.
  const visits =
    pulse?.visits_all_time != null && pulse.visits_from !== null
      ? { total: pulse.visits_all_time, from: pulse.visits_from, days: pulse.visits_by_day }
      : null;
  const bars = barHeights(
    visits !== null ? visits.days : people === null ? [] : (pulse?.people_new_by_day ?? [])
  );
  const barsName = visits !== null ? 'Visits each day' : 'New people each day';

  return (
    <article
      className={styles.instrument}
      aria-labelledby="pulse-people-label"
      data-testid="pulse-people"
    >
      <p className={`${styles.label} ${styles.people}`} id="pulse-people-label">
        <span className={styles.swatch} aria-hidden="true" />
        {visits !== null ? 'Visits by people' : 'People who have visited'}
      </p>
      <p className={styles.reading}>
        {visits !== null ? (
          <>
            <span className={styles.figure} data-testid="pulse-people-figure">
              {visits.total.toLocaleString('en-US')}
            </span>
            <span className={styles.unit}>
              {unitOf(visits.total, 'visit', 'visits')} since {dayWords(visits.from)}
            </span>
          </>
        ) : people !== null ? (
          <>
            <span className={styles.figure} data-testid="pulse-people-figure">
              {people.toLocaleString('en-US')}
            </span>
            <span className={styles.unit}>
              {unitOf(people, 'person', 'people')}
              {pulse?.counted_from ? ` since ${dayWords(pulse.counted_from)}` : ', all time'}
            </span>
          </>
        ) : pulse?.counted_from ? (
          <span className={styles.unread} data-testid="pulse-people-counting">
            counting from {dayWords(pulse.counted_from)}
          </span>
        ) : (
          <span className={styles.unread}>{UNREAD}</span>
        )}
      </p>
      <svg
        className={styles.bars}
        viewBox="0 0 320 34"
        role="img"
        aria-label={
          bars.length === 0
            ? `${barsName}, nothing counted yet`
            : `${barsName} for the last ${bars.length} days`
        }
      >
        {bars.map((height, day) => (
          <rect
            key={day}
            className={day === bars.length - 1 ? styles.today : styles.bar}
            x={2 + (316 / bars.length) * day + 1}
            y={22 - 20 * height}
            width={Math.max(1, 316 / bars.length - 2)}
            height={20 * height}
          />
        ))}
        <line className={styles.axis} x1="2" y1="22" x2="318" y2="22" />
        <text className={styles.number} x="2" y="33" textAnchor="start">
          30 days ago
        </text>
        <text className={styles.number} x="318" y="33" textAnchor="end">
          today
        </text>
      </svg>
      <p className={styles.detail}>
        Scanners, my own tools' and my own reads left out.{' '}
        <button
          type="button"
          className={styles.link}
          onClick={() => onOpenDoc('adrPeopleWhoHaveVisited')}
        >
          How a visitor is counted
        </button>
      </p>
    </article>
  );
}
