/**
 * A bar gauge's numbers (the tweaks pass, B2), kept out of the component so the
 * edge cases are tested: the share of the ceiling the fill draws, and the
 * meter's value for a screen reader, finite and within 0 to the ceiling
 * whatever the reading was (a negative, a NaN, a reading over its plan).
 */
export function gaugeMeter(
  value: number,
  max: number
): { share: number; now: number; max: number } {
  const ceiling = Number.isFinite(max) && max > 0 ? max : 0;
  const reading = Number.isFinite(value) ? Math.max(0, value) : 0;
  const now = Math.min(reading, ceiling);
  return { share: ceiling === 0 ? 0 : now / ceiling, now, max: ceiling };
}
