/**
 * Throwaway, coderabbitadr1110's proof request: a comment that shows a wire format with a date in it,
 * which the record allows.
 */

/** The wire sends a day as `2026-11-10`, four digits, two and two, so this reads the first ten characters. */
export function dayOf(stamp: string): string {
  return stamp.slice(0, 10);
}
