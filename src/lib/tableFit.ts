/**
 * When an Admin table stacks (ADR-083). A table asks how wide its own card
 * is, not how wide the screen is, because a desk can still hand a table a
 * card too narrow for its columns. When the card cannot give every column
 * enough room, the table stacks: each row becomes a small block of labels
 * and values. DataTable.tsx does the measuring and calls stacksAt below.
 */

/** A table of three or more columns can stack; two are a label and a value already. */
export const STACK_FROM_COLUMNS = 3;

/**
 * The least a column of words needs to read as a column, 7.5rem at the page's
 * 16 px: a short path, a container's name, the start of a message. The stacked
 * label's own column is the same 7.5rem.
 */
export const COLUMN_ROOM_PX = 120;

/**
 * The least a number or a short reading needs, 5.5rem: "3590 ms", "2.89 RU",
 * "12:04:31 PM" in the small mono type, each on one line.
 */
export const NARROW_ROOM_PX = 88;

/**
 * True when a box this wide cannot hold these columns side by side: `wide`
 * columns of words and `narrow` columns of numbers or short readings. A box not
 * laid out yet (0) is not stacked, so nothing is decided on a size nobody measured.
 */
export function stacksAt(boxWidth: number, wide: number, narrow = 0): boolean {
  const columns = wide + narrow;
  const room = wide * COLUMN_ROOM_PX + narrow * NARROW_ROOM_PX;
  return columns >= STACK_FROM_COLUMNS && boxWidth > 0 && boxWidth < room;
}

/**
 * A dotted name ("TheYard.Infrastructure.Cosmos.CosmosStore") in the pieces a
 * line may break between: each dot stays on the end of the piece before it, so
 * a narrow column breaks at a dot rather than inside a word.
 */
export function dottedParts(name: string): string[] {
  return name.split(/(?<=\.)/);
}

/** A token longer than this many characters has no word break a column can use: an address, a stack frame. */
export const LONG_TOKEN = 24;

/**
 * A sentence in the pieces a line may break between: words as they are, and a
 * token longer than LONG_TOKEN (an address, a path) split after each slash,
 * dot, question mark, ampersand or equals sign, so a narrow column breaks a
 * long address at its own punctuation and never inside a word. The pieces join
 * back to the sentence.
 */
export function addressParts(text: string): string[] {
  return text
    .split(/(\s+)/)
    .flatMap((token) => (token.length > LONG_TOKEN ? token.split(/(?<=[/.?&=])/) : [token]))
    .filter((part) => part.length > 0);
}
