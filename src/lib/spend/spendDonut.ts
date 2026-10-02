/**
 * The geometry of the donut by resource: one SVG arc per slice, clockwise from
 * the top. It is its own file because arc drawing has its own edge case (a
 * slice that is the whole) and nothing else on the card needs it.
 */
import type { CostSlice } from './costReport';

// #region spend-donut
/** One slice of the donut, ready to draw: its path, its share, and which colour step it takes. */
export type DonutArc = { name: string; path: string; share: number; tone: number };

/**
 * The donut: one arc per slice, clockwise from the top, each a share of the
 * whole. A slice that is the whole is drawn as two halves, because an SVG arc
 * whose ends meet draws nothing.
 */
export function donutArcs(slices: CostSlice[], radius = 80, inner = 50): DonutArc[] {
  const total = slices.reduce((sum, slice) => sum + slice.cost, 0);
  if (total <= 0) return [];
  const centre = radius;
  const point = (angle: number, r: number) => {
    const theta = (angle - 90) * (Math.PI / 180);
    return `${(centre + r * Math.cos(theta)).toFixed(2)},${(centre + r * Math.sin(theta)).toFixed(2)}`;
  };
  const arc = (from: number, to: number) => {
    const large = to - from > 180 ? 1 : 0;
    return [
      `M${point(from, radius)}`,
      `A${radius},${radius} 0 ${large} 1 ${point(to, radius)}`,
      `L${point(to, inner)}`,
      `A${inner},${inner} 0 ${large} 0 ${point(from, inner)}`,
      'Z',
    ].join(' ');
  };
  let start = 0;
  return slices.map((slice, index) => {
    const sweep = (slice.cost / total) * 360;
    const end = start + sweep;
    const path =
      sweep >= 359.99 ? `${arc(0, 180)} ${arc(180, 359.99)}` : arc(start, Math.max(start, end));
    start = end;
    return {
      name: slice.name,
      path,
      share: slice.share,
      tone: slice.name === 'Others' ? 0 : index + 1,
    };
  });
}
// #endregion spend-donut
