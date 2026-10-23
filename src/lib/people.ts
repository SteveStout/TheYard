/**
 * Does:      Names the shape /api/admin/visitors/people answers (people who have visited, counted once, all time),
 *            reads an answer into it, and says each figure in the words the Admin tab shows.
 * Does not:  Fetch (the tab and the Activity card do), draw anything, or decide who a person is (the Domain's
 *            VisitorKind rule does, on the server, for the activity card and this count alike).
 * Used by:   useAdminReads.ts, PeopleReadouts.tsx, people.test.ts.
 */

/** Where the count is read from. */
export const PEOPLE_URL = '/api/admin/visitors/people';

/** One UTC day of the thirty: the day and how many people were first seen on it. */
export type PeopleDay = { day: string; people: number };

/** The count as the server answers it (ADR: People who have visited, counted once). A null is not counted yet. */
export type PeopleCount = {
  people_all_time: number | null;
  people_new_today: number | null;
  people_new_month: number | null;
  scanners_left_out: number | null;
  /** The UTC day the count began, yyyy-mm-dd. */
  counted_from: string;
  /** The last thirty UTC days, oldest first, today last. */
  people_new_by_day: PeopleDay[];
  /** Where every figure is read from, named on the card beside it. */
  source: string;
};

const count = (value: unknown): value is number | null =>
  value === null || (typeof value === 'number' && Number.isFinite(value) && value >= 0);
const day = (value: unknown): value is string =>
  typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value);

/** An answer read into the shape, or null when it is not that shape, so the card says so rather than guessing. */
export function peopleFrom(answer: unknown): PeopleCount | null {
  if (typeof answer !== 'object' || answer === null) return null;
  const read = answer as Record<string, unknown>;
  if (
    !count(read.people_all_time) ||
    !count(read.people_new_today) ||
    !count(read.people_new_month) ||
    !count(read.scanners_left_out) ||
    !day(read.counted_from) ||
    typeof read.source !== 'string' ||
    !Array.isArray(read.people_new_by_day)
  ) {
    return null;
  }
  const days = read.people_new_by_day as unknown[];
  if (
    !days.every(
      (entry) =>
        typeof entry === 'object' &&
        entry !== null &&
        day((entry as Record<string, unknown>).day) &&
        typeof (entry as Record<string, unknown>).people === 'number'
    )
  ) {
    return null;
  }
  return {
    people_all_time: read.people_all_time,
    people_new_today: read.people_new_today,
    people_new_month: read.people_new_month,
    scanners_left_out: read.scanners_left_out,
    counted_from: read.counted_from,
    people_new_by_day: (days as PeopleDay[]).map((entry) => ({
      day: entry.day,
      people: entry.people,
    })),
    source: read.source,
  };
}

/** A count in words: the number with its thousands marked, or the word for a figure not counted yet. */
export function countWords(value: number | null): string {
  return value === null ? 'not counted yet' : value.toLocaleString('en-US');
}

/** The day the count began, as the card writes it: "24 Oct 2026". */
export function countedFromWords(day: string): string {
  const at = new Date(`${day}T00:00:00Z`);
  return Number.isNaN(at.getTime())
    ? day
    : at.toLocaleDateString('en-GB', {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'UTC',
      });
}

/** The month a "new this month" figure covers, as the card writes it: "October". */
export function monthWords(today: Date): string {
  return today.toLocaleDateString('en-US', { month: 'long', timeZone: 'UTC' });
}
