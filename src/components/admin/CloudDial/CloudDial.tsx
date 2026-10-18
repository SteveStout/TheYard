/**
 * One dial of a Cloud infrastructure card (ADR: The App Service plan, read every minute): a tick ring round
 * a whole track, the reading's arc in its tone from twelve o'clock, the amber and red marks on the
 * inner edge, a gold dot on the ring at the thirty-day 95th and a red dot at the peak, the big
 * figure with its absolute in the middle, and the dial's name under it.
 */
import { angleOf, arcPath, pointAt, ticks, type CloudDial as Dial } from '../../../lib/cloud';
import styles from './CloudDial.module.css';

const SIZE = 200;
const C = SIZE / 2;
const R = 72;

export function CloudDial({ dial, testId }: { dial: Dial; testId: string }) {
  const at = (percent: number, radius: number) => pointAt(C, C, radius, angleOf(percent));
  const mark = (percent: number, className: string) => {
    const inner = at(percent, R - 16);
    const outer = at(percent, R - 9);
    return <line className={className} x1={inner.x} y1={inner.y} x2={outer.x} y2={outer.y} />;
  };
  return (
    <figure className={styles.dial} data-testid={testId} data-tone={dial.tone}>
      <svg
        viewBox={`0 0 ${SIZE} ${SIZE}`}
        role="img"
        aria-label={`${dial.name}: ${dial.big}, ${dial.absolute}`}
      >
        <circle className={styles.face} cx={C} cy={C} r={R + 24} />
        {ticks().map((tick) => {
          const a = at(tick.percent, R + 13);
          const b = at(tick.percent, R + (tick.long ? 21 : 17));
          return (
            <line key={tick.percent} className={styles.tick} x1={a.x} y1={a.y} x2={b.x} y2={b.y} />
          );
        })}
        <circle className={styles.track} cx={C} cy={C} r={R} />
        {dial.percent !== null && dial.percent > 0 && (
          <path
            className={`${styles.arc} ${styles[dial.tone]}`}
            d={arcPath(C, C, R, 0, dial.percent)}
          />
        )}
        {mark(dial.amber, styles.amberMark)}
        {mark(dial.red, styles.redMark)}
        {dial.p95 !== null && (
          <circle className={styles.p95} cx={at(dial.p95, R).x} cy={at(dial.p95, R).y} r={6}>
            <title>{`95th over 30 days: ${Math.round(dial.p95)} %`}</title>
          </circle>
        )}
        {dial.peak !== null && (
          <circle
            className={styles.peak}
            cx={at(dial.peak, R + 9).x}
            cy={at(dial.peak, R + 9).y}
            r={4}
          >
            <title>{`Peak over 30 days: ${Math.round(dial.peak)} %`}</title>
          </circle>
        )}
        <text className={styles.big} x={C} y={C + 6} textAnchor="middle">
          {dial.big}
        </text>
        <text className={styles.absolute} x={C} y={C + 28} textAnchor="middle">
          {dial.absolute}
        </text>
      </svg>
      <figcaption className={styles.name}>{dial.name}</figcaption>
    </figure>
  );
}
