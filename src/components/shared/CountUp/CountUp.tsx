import { useEffect, useRef } from 'react';
import { countable, figureAt } from '../../../lib/countUp';
import styles from './CountUp.module.css';

/** How long a reading takes to arrive at its value: the ring's own fill time. */
const DURATION_MS = 400;

/**
 * A figure that eases up from nothing to its value the first time it arrives
 * (the operator's look), and not at all for a reader who asked for less
 * motion. The element's text is the finished figure from the first paint, in
 * the server's HTML and after the page is taken over, so a screen reader, a
 * crawler or a quick screenshot never reads a figure on its way (ADR-142).
 * The count is visual only: each frame writes the moving figure into
 * data-moving, which a pseudo-element draws over the finished one while the
 * finished one is drawn unseen and holds the box, so nothing beside it moves.
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
      // The finished figure only, in case a count was cut short by a new reading.
      target.removeAttribute('data-moving');
      return;
    }
    counted.current = true;
    let frame = 0;
    const start = performance.now();
    const step = (now: number) => {
      const t = (now - start) / DURATION_MS;
      if (t < 1) {
        target.setAttribute('data-moving', figureAt(figure, t));
        frame = window.requestAnimationFrame(step);
      } else target.removeAttribute('data-moving');
    };
    frame = window.requestAnimationFrame(step);
    return () => {
      window.cancelAnimationFrame(frame);
      target.removeAttribute('data-moving');
    };
  }, [text]);
  return (
    <span ref={node} className={styles.count}>
      <span>{text}</span>
    </span>
  );
}
