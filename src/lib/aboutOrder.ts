/**
 * Does:      Holds the About page's order, top to bottom, in one list (ADR-142, the addendum on the landing views; the
 *            ruling that the professional is woven between the personal, not clustered at the top): the head, How I
 *            lead, then work sections and personal rows taking turns, the voices split in two runs, Why I write, the
 *            couple's photographs and the close last. Each personal row names the side its photographs sit on, so the
 *            big and the medium seats alternate down the page.
 * Does not:  Read the document (aboutWeave.ts cuts it) or draw anything (AboutPage.tsx does); no React in here.
 * Used by:   AboutPage.tsx, aboutWeave.ts, aboutOrder.test.ts.
 */

/** A part of the page that is not a personal row. */
export type AboutPart =
  'head' | 'lead' | 'work' | 'voices' | 'voices-more' | 'coaching' | 'write' | 'strip' | 'close';

/** Where a row's photographs sit: the 7/12 seat on the left, the 5/12 seat on the right, or across the whole row. */
export type RowSide = 'left' | 'right' | 'wide';

/** One place on the page: a part, or a personal row named by the document's own third-level heading. */
export type AboutEntry = { part: AboutPart } | { row: string; side: RowSide };

/** The page, top to bottom, as the About design draws it. */
export const ABOUT_ORDER: readonly AboutEntry[] = [
  { part: 'head' },
  { part: 'lead' },
  { row: 'St. Charles', side: 'left' },
  { part: 'work' },
  { row: 'History', side: 'right' },
  { row: 'Food', side: 'left' },
  { part: 'voices' },
  { row: 'Games', side: 'right' },
  { row: 'Modding', side: 'left' },
  { part: 'voices-more' },
  { part: 'coaching' },
  { row: 'Too many house plants', side: 'right' },
  { row: 'Freya and Vincent', side: 'wide' },
  { part: 'write' },
  { part: 'strip' },
  { part: 'close' },
];

/** The voices drawn in the first run; the rest are drawn after Modding. */
export const VOICES_FIRST = 3;

/** The personal rows' titles, in the page's order. */
export const ROW_ORDER: readonly string[] = ABOUT_ORDER.flatMap((entry) =>
  'row' in entry ? [entry.row] : []
);

/**
 * The page's order with every row the document has: a row the list does not name is set after the last named row,
 * its side taking its turn after the one before it, so a block added to the document is drawn, never dropped.
 * @param titles the personal rows the document holds, by title
 */
export function placed(titles: readonly string[]): AboutEntry[] {
  const named = ABOUT_ORDER.filter((entry) => !('row' in entry) || titles.includes(entry.row));
  const extra = titles.filter((title) => !ROW_ORDER.includes(title));
  if (extra.length === 0) return named;
  const last = named.reduce((at, entry, index) => ('row' in entry ? index : at), 0);
  const before = named[last];
  let side: RowSide = 'row' in before && before.side === 'left' ? 'right' : 'left';
  const more = extra.map((row) => {
    const entry: AboutEntry = { row, side };
    side = side === 'left' ? 'right' : 'left';
    return entry;
  });
  return [...named.slice(0, last + 1), ...more, ...named.slice(last + 1)];
}
