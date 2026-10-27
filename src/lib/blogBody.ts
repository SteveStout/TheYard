/**
 * Does:      Reads a post's text into the pieces the card draws (ADR: Every LinkedIn post is also a blog post, the
 *            addendum on a post that reads as a blog): headings, paragraphs and lists, and inside them bold, code, links
 *            and hashtags, from the small Markdown the posts are written in. Plain text with no marks comes out as
 *            paragraphs, so a post kept as LinkedIn had it still reads.
 * Does not:  Draw anything (BlogPostCard does) or allow a link that is not http or https.
 * Used by:   BlogPostCard.tsx.
 */

/** One run of text inside a paragraph, a heading or a list item. */
export type BodyInline =
  | { kind: 'text' | 'bold' | 'code' | 'tag'; text: string }
  | { kind: 'link'; text: string; href: string };

/** One block of a post, top to bottom. A paragraph keeps its line breaks as the writer put them. */
export type BodyBlock =
  | { kind: 'heading'; text: BodyInline[] }
  | { kind: 'paragraph'; lines: BodyInline[][] }
  | { kind: 'list'; items: BodyInline[][] };

/**
 * The marks a run can carry, tried in this order at each position: a link [words](https://...), code in backticks,
 * bold in double stars, and a hashtag. Anything the pattern does not match stays as it was written.
 */
const MARKS =
  /\[([^\]\n]+)\]\((https?:\/\/[^\s)]+)\)|`([^`\n]+)`|\*\*([^*\n]+?)\*\*|(#[A-Za-z][A-Za-z0-9_]*)/g;

/** The runs of one line: plain text between the marks, and each mark as what it is. */
export function inlineParts(line: string): BodyInline[] {
  const parts: BodyInline[] = [];
  let last = 0;
  for (const match of line.matchAll(MARKS)) {
    const at = match.index ?? 0;
    if (at > last) parts.push({ kind: 'text', text: line.slice(last, at) });
    const [whole, linkText, href, code, bold, tag] = match;
    if (href !== undefined) parts.push({ kind: 'link', text: linkText, href });
    else if (code !== undefined) parts.push({ kind: 'code', text: code });
    else if (bold !== undefined) parts.push({ kind: 'bold', text: bold });
    else if (tag !== undefined) parts.push({ kind: 'tag', text: tag });
    last = at + whole.length;
  }
  if (last < line.length) parts.push({ kind: 'text', text: line.slice(last) });
  return parts;
}

/** A line that opens a heading: one to three hashes, a space, the words. */
const HEADING = /^#{1,3}\s+(.+)$/;

/** A line that is a list item: a dash or a star, a space, the words. */
const ITEM = /^[-*]\s+(.+)$/;

/**
 * The blocks of a post. A blank line ends a paragraph; a heading line stands alone; item lines that follow one
 * another make one list; every other line joins the paragraph under way, as a line of its own.
 */
export function bodyBlocks(body: string): BodyBlock[] {
  const blocks: BodyBlock[] = [];
  let paragraph: BodyInline[][] | null = null;
  let list: BodyInline[][] | null = null;
  const close = () => {
    if (paragraph) blocks.push({ kind: 'paragraph', lines: paragraph });
    if (list) blocks.push({ kind: 'list', items: list });
    paragraph = null;
    list = null;
  };
  for (const raw of body.split('\n')) {
    const line = raw.trimEnd();
    const heading = HEADING.exec(line);
    const item = ITEM.exec(line);
    if (line.trim() === '') {
      close();
    } else if (heading) {
      close();
      blocks.push({ kind: 'heading', text: inlineParts(heading[1]) });
    } else if (item) {
      if (paragraph) close();
      (list ??= []).push(inlineParts(item[1]));
    } else {
      if (list) close();
      (paragraph ??= []).push(inlineParts(line));
    }
  }
  close();
  return blocks;
}
