import { useEffect, useRef } from 'react';
import { countable, figureAt } from '../lib/countUp';
import styles from './CountUp.module.css';

/** How long a reading takes to arrive at its value: the ring's own fill time. */
const DURATION_MS = 400;

/**
 * A figure that eases up from nothing to its value the first time it arrives
 * (the operator's look), and not at all for a reader who asked for less
 * motion. The finished figure holds the box from the start, drawn unseen
 * behind the moving one, so nothing beside it moves while it counts.
 *
 * Each frame writes the moving figure's text straight into its node rather
 * than through React's state (1.0.3.20): a render per frame for every
 * figure is work the count does not need. React still owns the text, and sets
 * the finished figure whenever the reading changes.
 */
export function CountUp({ text }: { text: string }) {
  const node = useRef<HTMLSpanElement>(null);
  const counted = useRef(false);
  useEffect(() => {
    const target = node.current;
    const figure = countable(text);
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (target === null) return;
    if (figure === null || counted.current || still) {
      // The finished figure, in case a count was cut short by a new reading.
      target.textContent = text;
      return;
    }
    counted.current = true;
    let frame = 0;
    const start = performance.now();
    const step = (now: number) => {
      const t = (now - start) / DURATION_MS;
      target.textContent = figureAt(figure, t);
      if (t < 1) frame = window.requestAnimationFrame(step);
    };
    frame = window.requestAnimationFrame(step);
    return () => window.cancelAnimationFrame(frame);
  }, [text]);
  return (
    <span className={styles.count} data-value={text}>
      <span ref={node}>{text}</span>
    </span>
  );
}
