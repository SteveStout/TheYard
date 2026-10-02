/**
 * A duration in words, for the Admin cards that print one. A reading of
 * 194912 ms printed raw is six digits to count before it reads as three
 * minutes, so every figure is grouped by thousands, and from ten seconds up it
 * is written in seconds, where the number is short enough to read at a glance.
 * Below a millisecond the reading says so rather than printing a zero or a
 * long decimal.
 */

/** Ten seconds: from here up, a duration is written in seconds. */
export const SECONDS_FROM_MS = 10_000;

/** Grouped by thousands, at most one decimal place: the format both units are written in. */
const oneDecimal = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 });

/**
 * "under 1 ms", "298.4 ms", "1,183 ms", "68.5 s" or "194.9 s". Fractions are
 * kept to one decimal place in both units.
 */
export function durationWords(ms: number): string {
  if (ms < 1) return 'under 1 ms';
  if (ms < SECONDS_FROM_MS) return `${oneDecimal.format(ms)} ms`;
  return `${oneDecimal.format(ms / 1000)} s`;
}
