/**
 * A ring gauge's arithmetic (ADR: The glass look, the addendum on the
 * operator's look): a ratio or a level as an arc over a track, the number in
 * words beside it or inside it. React-free, so the arc a component draws is a
 * rule a test reads.
 */

/** The sizes a ring is drawn at, and the stroke each carries: one table, so no component writes its own. */
export const RING = {
  /** Beside the store toggle: the segment's readiness. */
  tiny: { size: 20, stroke: 3 },
  /** Beside a card's line on the inventory. */
  small: { size: 28, stroke: 4 },
  /** On a stat tile. */
  tile: { size: 44, stroke: 5 },
  /** On a page: a vehicle's auction, the evidence strip. */
  page: { size: 88, stroke: 8 },
  /** Beside the open Admin card. */
  large: { size: 200, stroke: 14 },
} as const;

export type RingSize = keyof typeof RING;

export type RingArc = {
  /** The circle's radius inside the box, clear of the stroke. */
  radius: number;
  /** The circle's whole length. */
  length: number;
  /** How much of it the value draws, from twelve o'clock clockwise. */
  drawn: number;
  /** The value's share of the whole, from 0 to 1. */
  share: number;
};

/** The arc for a value out of a whole; nothing drawn when there is no whole or no number. */
export function ringArc(value: number, max: number, size: number, stroke: number): RingArc {
  const radius = (size - stroke) / 2;
  const length = 2 * Math.PI * radius;
  const share = max > 0 && Number.isFinite(value) ? Math.max(0, Math.min(1, value / max)) : 0;
  return {
    radius: Math.round(radius * 100) / 100,
    length: Math.round(length * 100) / 100,
    drawn: Math.round(length * share * 100) / 100,
    share,
  };
}

// #region ring-marker
/**
 * Where the gold marker sits (the tweaks pass, A3): the end of the fill, on the
 * circle's centre line, so a ring at 100 per cent still reads as a gauge that
 * came round rather than a plain circle. Null when nothing is drawn.
 */
export function ringMarker(
  share: number,
  size: number,
  stroke: number
): { x: number; y: number } | null {
  if (!(share > 0)) return null;
  const radius = (size - stroke) / 2;
  const middle = size / 2;
  const angle = Math.min(1, share) * 2 * Math.PI;
  return {
    x: Math.round((middle + radius * Math.sin(angle)) * 100) / 100,
    y: Math.round((middle - radius * Math.cos(angle)) * 100) / 100,
  };
}

/**
 * The graduations round a large ring (B2): thirty-six ticks outside the stroke,
 * every ninth a major one, twice as long, at the quarters. Drawn outside the
 * ring's box, which is its size from the first paint, so they move nothing.
 */
export function ringGraduations(
  size: number,
  count = 36
): { x1: number; y1: number; x2: number; y2: number; major: boolean }[] {
  const middle = size / 2;
  const inner = middle + 3;
  return Array.from({ length: count }, (_, index) => {
    // Major where a quarter lands on a tick; a count not divisible by four has
    // majors only at the quarters it does land on.
    const major = (index * 4) % count === 0;
    const outer = inner + (major ? 6 : 3);
    const angle = (index / count) * 2 * Math.PI;
    const at = (radius: number) => ({
      x: Math.round((middle + radius * Math.sin(angle)) * 100) / 100,
      y: Math.round((middle - radius * Math.cos(angle)) * 100) / 100,
    });
    const from = at(inner);
    const to = at(outer);
    return { x1: from.x, y1: from.y, x2: to.x, y2: to.y, major };
  });
}
// #endregion ring-marker
