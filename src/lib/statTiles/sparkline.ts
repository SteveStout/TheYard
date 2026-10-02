/**
 * The small line under a tile: its points for SVG polylines, and the caption
 * that says what span the lines cover. It is its own file because drawing the
 * line is geometry, apart from the rules that decide a tile's number and colour.
 */

/**
 * The caption that says what time span the small lines under the tiles cover.
 * A sparkline has no axis, so without this it says nothing about its own width.
 * The number over a line is always "now"; only the line follows the chosen window.
 */
export function sparkCaption(
  stretch: string,
  state: 'hour' | 'reading' | 'kept' | 'not-kept'
): string {
  switch (state) {
    case 'hour':
      return 'The line under a tile is the last hour.';
    case 'reading':
      return `Reading the ${stretch}; the lines are still the last hour.`;
    case 'not-kept':
      return `The ${stretch} is not kept here, so the lines are still the last hour.`;
    case 'kept':
      return `The line under a tile is the ${stretch}, from the minutes this site keeps. The number over it is still now.`;
  }
}

// #region spark
/**
 * The sparkline under a tile, as point lists for SVG polylines. There is one
 * list per unbroken run, so an unmeasured minute is a gap in the line, not a
 * drop to zero. The scale starts at zero: a line scaled from its own smallest
 * value makes a few megabytes of drift look like a cliff. A run of a single
 * reading is dropped, because a one-point polyline draws nothing.
 */
export function sparkRuns(values: (number | null)[], width: number, height: number): string[] {
  const measured = values.filter((value): value is number => value !== null);
  if (measured.length < 2) return [];
  const top = Math.max(...measured, 0);
  const step = values.length > 1 ? width / (values.length - 1) : 0;
  const runs: string[] = [];
  let run: string[] = [];
  // Ends the current run, keeping it only if it has at least two points.
  const close = () => {
    if (run.length > 1) runs.push(run.join(' '));
    run = [];
  };
  values.forEach((value, index) => {
    if (value === null) {
      close();
      return;
    }
    const x = Math.round(index * step * 10) / 10;
    // One unit of room top and bottom, so a flat line at zero or at the top is not clipped.
    const y =
      top <= 0 ? height - 1 : Math.round((height - 1 - (value / top) * (height - 2)) * 10) / 10;
    run.push(`${x},${y}`);
  });
  close();
  return runs;
}
// #endregion spark
