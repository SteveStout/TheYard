/**
 * Does:      Runs the comet on the short path's quality line (ADR: The sales pages set the site's look, the motions): a
 *            gold dot with a short gold tail runs the line from its first point to its last at an even pace, 4.5
 *            seconds a run, three runs. The chart is stretched to its tile, so the dot is an HTML mark laid over it,
 *            placed from the line's own points; the tail is the line's own path, dashed. Loaded only once its card is
 *            seen, so the first screen never waits for it.
 * Does not:  Decide when it runs (useFirstSeen.ts and the tile do) or draw the dot and tail's look (sales.css does).
 * Used by:   QualityTrendTile.tsx, comet.test.ts.
 */

/** One run's length, and how many runs a sighting plays. */
export const COMET = { ms: 4500, runs: 3, steps: 90, tail: 70 } as const;

/** A box on the screen. */
export type Box = { left: number; top: number; width: number; height: number };

/**
 * The dot's keyframes: each point of the line, in the chart's own units, placed over the stretched chart in the tile's
 * pixels, faded in after the first point and out at the last.
 */
export function cometFrames(
  points: readonly { x: number; y: number }[],
  view: { width: number; height: number },
  chart: Box,
  holder: Pick<Box, 'left' | 'top'>
): { left: string; top: string; opacity: number }[] {
  const last = points.length - 1;
  return points.map((point, index) => ({
    left: `${chart.left - holder.left + (point.x / view.width) * chart.width}px`,
    top: `${chart.top - holder.top + (point.y / view.height) * chart.height}px`,
    opacity: index === 0 || index === last ? 0 : 1,
  }));
}

/** Starts the comet on the dot and the tail; the animations it started, to cancel when the card leaves the window. */
export function runComet(dot: HTMLElement, tail: SVGPathElement): Animation[] {
  const svg = tail.ownerSVGElement;
  const holder = dot.offsetParent;
  if (!svg || !holder) return [];
  const view = svg.viewBox.baseVal;
  const length = tail.getTotalLength();
  const points = Array.from({ length: COMET.steps + 1 }, (_, index) =>
    tail.getPointAtLength((length * index) / COMET.steps)
  );
  const frames = cometFrames(
    points,
    view,
    svg.getBoundingClientRect(),
    holder.getBoundingClientRect()
  );
  const timing = { duration: COMET.ms, iterations: COMET.runs, easing: 'linear' };
  return [
    dot.animate(frames, timing),
    tail.animate(
      [
        { strokeDashoffset: COMET.tail, opacity: 1 },
        { strokeDashoffset: -1000, opacity: 1 },
      ],
      timing
    ),
  ];
}
