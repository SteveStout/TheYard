import { useEffect, useState } from 'react';
import { RING, ringArc, ringGraduations, ringMarker, type RingSize } from '../lib/ring';
import styles from './Ring.module.css';

/**
 * A ratio or a level as a ring (ADR: The glass look, the addendum on the
 * operator's look): a track, the value's arc from twelve o'clock, and a second
 * arc inside it when a second series is read against the same whole. The
 * number is always in words, inside the ring or beside it, so the arc is never
 * the only thing that carries it. It fills from nothing on its first paint,
 * once, over the token's time, and not at all for a reader who asked for less
 * motion; a ring never moves the page, because its box is its size from the start.
 */
export function Ring({
  value,
  max,
  size = 'tile',
  tone = 'first',
  second,
  inside,
  label,
  testId,
  graduated = false,
}: {
  value: number;
  max: number;
  size?: RingSize;
  /** The outer arc's colour: the first series (the accent), or the second (the gold) for a ring that is one series of its own. */
  tone?: 'first' | 'second';
  /** A second series against the same whole, drawn inside the first in the second colour. */
  second?: { value: number; max: number };
  /** Short words drawn in the middle: the number, or the state. */
  inside?: string;
  /** What the ring says to a screen reader; null when the words beside it already say it. */
  label: string | null;
  testId?: string;
  /** Thirty-six graduation ticks round the outside (the tweaks pass, B2): the large rings of This hour. */
  graduated?: boolean;
}) {
  const { size: box, stroke } = RING[size];
  const outer = ringArc(value, max, box, stroke);
  const innerBox = box - 2 * stroke - 4;
  const inner = second === undefined ? null : ringArc(second.value, second.max, innerBox, stroke);
  // First paint with nothing drawn, then the value: the transition in the
  // sheet does the filling, once, and is off under reduced motion.
  const [shown, setShown] = useState(false);
  useEffect(() => {
    const frame = window.requestAnimationFrame(() => setShown(true));
    return () => window.cancelAnimationFrame(frame);
  }, []);
  const middle = box / 2;
  const turn = `rotate(-90 ${middle} ${middle})`;
  // The gold marker at the fill's end (A3): a full ring still reads as a gauge.
  const marker = ringMarker(outer.share, box, stroke);
  return (
    <svg
      className={`${styles.ring} ${styles[size]}`}
      viewBox={`0 0 ${box} ${box}`}
      width={box}
      height={box}
      role={label === null ? undefined : 'img'}
      aria-label={label ?? undefined}
      aria-hidden={label === null ? true : undefined}
      focusable="false"
      data-testid={testId}
      data-share={outer.share.toFixed(3)}
    >
      <circle
        className={styles.track}
        cx={middle}
        cy={middle}
        r={outer.radius}
        strokeWidth={stroke}
      />
      <circle
        className={`${styles.arc} ${tone === 'second' ? styles.second : styles.first}`}
        cx={middle}
        cy={middle}
        r={outer.radius}
        strokeWidth={stroke}
        strokeDasharray={`${shown ? outer.drawn : 0} ${outer.length}`}
        transform={turn}
      />
      {graduated &&
        ringGraduations(box).map((tick) => (
          <line
            key={`${tick.x1},${tick.y1}`}
            className={tick.major ? `${styles.tick} ${styles.tickMajor}` : styles.tick}
            x1={tick.x1}
            y1={tick.y1}
            x2={tick.x2}
            y2={tick.y2}
            data-testid="ring-tick"
          />
        ))}
      {inner !== null && (
        <>
          <circle
            className={styles.track}
            cx={middle}
            cy={middle}
            r={inner.radius}
            strokeWidth={stroke}
          />
          <circle
            className={`${styles.arc} ${styles.second}`}
            cx={middle}
            cy={middle}
            r={inner.radius}
            strokeWidth={stroke}
            strokeDasharray={`${shown ? inner.drawn : 0} ${inner.length}`}
            transform={turn}
          />
        </>
      )}
      {marker !== null && (
        <circle
          className={`${styles.marker} ${shown ? styles.markerShown : ''} ${tone === 'second' ? styles.markerOnGold : ''}`}
          cx={marker.x}
          cy={marker.y}
          r={Math.max(1.5, stroke * 0.42)}
          data-testid="ring-marker"
        />
      )}
      {inside !== undefined && (
        <text
          className={styles.inside}
          x={middle}
          y={middle}
          textAnchor="middle"
          dominantBaseline="central"
        >
          {inside}
        </text>
      )}
    </svg>
  );
}
