/**
 * The arithmetic behind the machine charts (ADR: What the machines are doing,
 * the addendum on drawing them). The same shape as the activity chart's lib:
 * plain functions over plain data, no React, so the drawing is a few paths and
 * the reasoning is testable on its own.
 */

/** One reading. A null value is a gap rather than a zero: the first processor share has nothing to compare against. */
export type ChartPoint = { at: string; value: number | null };

export type ChartSeries = { key: string; name: string; points: ChartPoint[] };

export const MACHINE_CHART = {
  width: 720,
  height: 170,
  left: 44,
  right: 12,
  top: 12,
  bottom: 24,
} as const;

/**
 * The top of the axis: the highest reading rounded up to something a person
 * reads, never zero. A percentage chart passes 100 as the floor so a quiet
 * hour is drawn along the bottom of a full axis rather than filling it.
 */
export function ceilingFor(series: ChartSeries[], atLeast = 1): number {
  let top = atLeast;
  for (const line of series) {
    for (const point of line.points) {
      if (point.value !== null && point.value > top) top = point.value;
    }
  }
  if (top <= 1) return 1;
  const magnitude = Math.pow(10, Math.floor(Math.log10(top)));
  for (const step of [1, 2, 2.5, 5, 10]) {
    const candidate = step * magnitude;
    if (top <= candidate) return Number(candidate.toFixed(4));
  }
  return 10 * magnitude;
}

/**
 * The line, with a break wherever a reading is missing: a gap drawn as a
 * straight line across it would be a number nobody measured.
 */
export function pathFor(points: ChartPoint[], ceiling: number): string {
  const innerWidth = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  const innerHeight = MACHINE_CHART.height - MACHINE_CHART.top - MACHINE_CHART.bottom;
  const step = points.length <= 1 ? 0 : innerWidth / (points.length - 1);
  let path = '';
  let penDown = false;
  points.forEach((point, index) => {
    if (point.value === null) {
      penDown = false;
      return;
    }
    const x = MACHINE_CHART.left + index * step;
    const y =
      MACHINE_CHART.top + innerHeight - (Math.min(point.value, ceiling) / ceiling) * innerHeight;
    path += `${path === '' || !penDown ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)} `;
    penDown = true;
  });
  return path.trim();
}

/** Up to three evenly spaced indexes to label, first and last always. */
export function ticks(count: number): number[] {
  if (count <= 1) return count === 1 ? [0] : [];
  if (count === 2) return [0, 1];
  return [0, Math.floor((count - 1) / 2), count - 1];
}

/** A reading's time as a clock reads it, which is the only axis a one-hour window needs. */
export function clockLabel(at: string): string {
  const when = new Date(at);
  return Number.isNaN(when.getTime())
    ? ''
    : `${String(when.getHours()).padStart(2, '0')}:${String(when.getMinutes()).padStart(2, '0')}`;
}
