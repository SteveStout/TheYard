// An error message as the Admin tab's errors card shows it (ADR: Error handling,
// one shape everywhere). A browser error often quotes an address, a module URL
// or a file path, one long run of characters with no space in it; set as a
// word, it sized its column past the card and the table scrolled sideways on a
// desk (found running the browser suite from a clean clone on 28 September).
// An address is an identifier, not a word, so it is marked as one and may break
// at any character, the way the .mono cells do; the words around it still
// break only between words.

/** One piece of a message: plain words, or an identifier that may break anywhere. */
export type MessagePart = { text: string; identifier: boolean };

// A scheme (http://, file://), or a path of two or more segments, or any run
// longer than a word is: thirty characters with no space is an identifier.
const IDENTIFIER = /^(?:[a-z][a-z0-9+.-]*:\/\/\S+|\S*[/\\]\S*[/\\]\S*|\S{30,})$/i;

/** Splits a message into words and identifiers, keeping every character and the spaces between. */
export function messageParts(message: string): MessagePart[] {
  const parts: MessagePart[] = [];
  for (const piece of message.split(/(\s+)/)) {
    if (piece === '') continue;
    const identifier = !/^\s+$/.test(piece) && IDENTIFIER.test(piece);
    const last = parts[parts.length - 1];
    if (last && !last.identifier && !identifier) {
      last.text += piece;
    } else {
      parts.push({ text: piece, identifier });
    }
  }
  return parts;
}
