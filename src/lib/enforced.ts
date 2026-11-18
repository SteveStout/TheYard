/**
 * Does:      Reads and words the marks in the sidebar's Decision Records index (ADR-029, the addendum on the
 *            marks): which records a test holds and which review holds. The API counts, for each record, the rules
 *            of ADR-075's table that cite it, the reading each record's own "Enforced by" section is built from,
 *            so a mark is never typed and cannot disagree with the record it sits beside.
 * Does not:  Read on the first load: the section asks when it is opened, so a reader who never opens the index
 *            never pays for it. Draw anything.
 * Used by:   useRecordMarks.ts, SideNav.tsx, RailDocRow.tsx, RailHeldKey.tsx, enforced.test.ts.
 */
import { counted } from './working';

/** How many of the rulebook's rules cite each record, by its three-digit number ("029"). */
export type Enforced = Record<string, number>;

/** Who holds a record: a test, when a rule cites it, or review, when none does. */
export type HeldBy = 'test' | 'review';

// #region enforced-seam
export async function fetchEnforced(signal?: AbortSignal): Promise<Enforced | null> {
  try {
    // Opening a section is not a request for this, so it never locks the page.
    const response = await counted(() => fetch('/api/docs/enforced', { signal }), {
      background: true,
    });
    if (!response.ok) return null;
    return (await response.json()) as Enforced;
  } catch {
    // An index without its marks is still the index; the rows just carry none.
    return null;
  }
}
// #endregion enforced-seam

/** The mark for record <number>, or null when the marks are not read or the API does not know the record. */
export function heldBy(enforced: Enforced | null, number: string | undefined): HeldBy | null {
  if (!enforced || !number || !(number in enforced)) return null;
  return enforced[number] > 0 ? 'test' : 'review';
}

/** What a mark says to a screen reader and in its tooltip, with the count when a test holds the record. */
export function heldByText(enforced: Enforced, number: string): string {
  const rules = enforced[number] ?? 0;
  if (rules === 0) return 'Held by review';
  return rules === 1
    ? 'Held by a test: one rule cites it'
    : `Held by tests: ${rules} rules cite it`;
}

/** The mark a record's row carries, or null for none: who holds it, and the words that say so. */
export function recordMark(
  enforced: Enforced | null,
  number: string | undefined
): { by: HeldBy; text: string } | null {
  const by = heldBy(enforced, number);
  return by && enforced && number ? { by, text: heldByText(enforced, number) } : null;
}

/** How many records each holder holds, for the key above the index. */
export function heldCounts(enforced: Enforced): { test: number; review: number } {
  const counts = Object.values(enforced);
  const test = counts.filter((rules) => rules > 0).length;
  return { test, review: counts.length - test };
}
