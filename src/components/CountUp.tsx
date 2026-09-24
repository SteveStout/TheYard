import { useEffect, useRef, useState } from 'react';
import { countable, figureAt } from '../lib/countUp';
import styles from './CountUp.module.css';

/** How long a reading takes to arrive at its value: the ring's own fill time. */
const DURATION_MS = 400;

/**
 * A figure that eases up from nothing to its value the first time it arrives
 * (the operator's look), and not at all for a reader who asked for less
 * motion. The finished figure holds the box from the start, drawn unseen
 * behind the moving one, so nothing beside it moves while it counts.
 */
export function CountUp({ text }: { text: string }) {
  const [shown, setShown] = useState(text);
  const counted = useRef(false);
  useEffect(() => {
    const figure = countable(text);
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (figure === null || counted.current || still) {
      setShown(text);
      return;
    }
    counted.current = true;
    let frame = 0;
    const start = performance.now();
    const step = (now: number) => {
      const t = (now - start) / DURATION_MS;
      setShown(figureAt(figure, t));
      if (t < 1) frame = window.requestAnimationFrame(step);
    };
    frame = window.requestAnimationFrame(step);
    return () => window.cancelAnimationFrame(frame);
  }, [text]);
  return (
    <span className={styles.count} data-value={text}>
      <span>{shown}</span>
    </span>
  );
}
