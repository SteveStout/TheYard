/**
 * When an Admin table stacks (1.0.3.31; ADR: The tweaks pass, the addendum on
 * tables that fit their card). 1.0.3.30 stacked a table of three or more columns
 * under the phone step, 640, and left every desk a table. The pictures of
 * 1.0.3.29 showed that a desk is not room enough either: the store's nine
 * columns in a card about 700 wide at 1024 were cut at the card's edge and broke
 * its times one character a line. The question is the card's width against the
 * table's columns, not the screen's, so the table measures its own box and
 * stacks when that box cannot give every column a column's room.
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
