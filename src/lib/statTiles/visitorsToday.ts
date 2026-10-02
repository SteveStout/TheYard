/**
 * Today's visitor count, picked out of the activity report's list of days for
 * the visitors tile. It is its own file because it reads a different report
 * from the one the other tiles read, and the rule for which count to trust
 * belongs beside the one function that applies it.
 */

/**
 * Today's visitor count from an activity report's list of days. The report's
 * days are UTC days, so "today" is the UTC date, which is why the tile says
 * UTC. No row for today means zero. Prefer `people`: the `humans` count also
 * includes App Service's own requests from the loopback address, so it is
 * only a fallback for a report that has no `people` field.
 */
export function visitorsOn(
  days: { day: string; humans: number; people?: number }[],
  now: Date
): number {
  const today = now.toISOString().slice(0, 10);
  const entry = days.find((candidate) => candidate.day === today);
  return entry ? (entry.people ?? entry.humans) : 0;
}
