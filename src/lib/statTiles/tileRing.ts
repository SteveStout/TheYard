/**
 * The ring gauge a tile may draw beside its number: a share of a known whole,
 * and that share as an SVG stroke dash. It is its own file because both the
 * tile rules and the drawing code need it, and neither needs the other.
 */
import type { TileRing } from './tileTypes';

/** A part of a whole as a ring. Returns undefined when there is no whole to measure against. */
export function ringOf(part: number, whole: number, label: string): TileRing | undefined {
  if (!(whole > 0) || Number.isNaN(part)) return undefined;
  return { share: Math.min(1, Math.max(0, part / whole)), label };
}

/**
 * The ring as an SVG stroke dash: the circle's full length, and how much of it
 * to leave undrawn. Both are rounded to one decimal place.
 */
export function ringStroke(share: number, radius: number): { length: number; gap: number } {
  const length = 2 * Math.PI * radius;
  const held = Math.min(1, Math.max(0, share));
  return { length: Math.round(length * 10) / 10, gap: Math.round(length * (1 - held) * 10) / 10 };
}
