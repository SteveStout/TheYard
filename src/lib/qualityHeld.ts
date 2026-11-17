/**
 * Does:      Says, from the quality history alone, whether each version held its quality numbers: a number holds when
 *            it is no worse than the version before (a fall, or a rise of a number that is better higher, reads
 *            "better"), and the history holds since the first version after the last one that made any number worse
 *            (ADR-130). One meaning of "held" for the Home line, the header badge and the
 *            changelog's Quality column, computed here and never typed.
 * Does not:  Read a file (the caller hands the rows in), draw anything, or judge against a limit: the gate's ratchet
 *            does that (.github/quality.py).
 * Used by:   Landing.tsx, GateBadge.tsx, qualityHeld.test.ts.
 */

// #region quality-held
/** One reading of the history as the file keeps it: the version, its day, who measured it and its numbers. */
export type HistoryRow = {
  version: string;
  date: string;
  commit?: string;
  source: string;
  numbers: Record<string, number | null | undefined>;
};

/** Whether one number moved the wrong way from one reading to the next; a missing reading on either side is not. */
function worse(
  key: string,
  before: number | null | undefined,
  after: number | null | undefined
): boolean {
  if (typeof before !== 'number' || typeof after !== 'number') return false;
  return key.endsWith('_higher_is_better') ? after < before : after > before;
}

/** The readings the gate measured, in the history's order: a pre-scan reading is a baseline, not a version. */
export function measured<T extends HistoryRow>(rows: readonly T[]): T[] {
  return rows.filter((row) => row.source === 'quality.py');
}

/** How many of a version's numbers held against the reading before it, of how many it measured. */
export function heldAt(
  row: HistoryRow,
  before: HistoryRow | undefined
): { held: number; of: number; better: number } {
  const keys = Object.keys(row.numbers).filter((key) => typeof row.numbers[key] === 'number');
  if (!before) return { held: keys.length, of: keys.length, better: 0 };
  const held = keys.filter((key) => !worse(key, before.numbers[key], row.numbers[key])).length;
  const better = keys.filter((key) => worse(key, row.numbers[key], before.numbers[key])).length;
  return { held, of: keys.length, better };
}

/**
 * The run the history holds now: the first reading after the last one that made any number worse, and how many
 * readings that run counts, today's included. Null when the newest reading itself made a number worse.
 */
export function heldSince(
  rows: readonly HistoryRow[]
): { version: string; date: string; releases: number } | null {
  let start = 0;
  for (let i = 1; i < rows.length; i++) {
    const before = rows[i - 1]!;
    const row = rows[i]!;
    if (Object.keys(row.numbers).some((key) => worse(key, before.numbers[key], row.numbers[key])))
      start = i + 1;
  }
  const first = rows[start];
  return first ? { version: first.version, date: first.date, releases: rows.length - start } : null;
}
// #endregion quality-held
