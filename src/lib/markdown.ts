/**
 * A served document, markdown to HTML (ADR: Docs and testing) (ADR: Code that
 * reads like code).
 *
 * This file is the only importer of `marked` and of the highlighter, and it is
 * loaded on demand: the documents sidebar asks for it the first time a reader
 * opens a record, through a dynamic import, so the inventory page's bundle
 * carries neither library. Until 1.0.0.141 both rode in the main chunk and
 * every visitor paid for the renderer whether or not they read a document
 * (ADR: Code that reads like code, addendum). Plain TypeScript with no React
 * import, which is the rule for everything under src/lib.
 */
import { marked } from 'marked';
import { highlight, grammarFor } from './highlight';
import { swatchSheet } from './swatches';
import { styleSheet as tokenSheet } from './styleSheet';
import { STYLE_FENCES, styleBlock, type StyleFence } from '../library/styleBlocks';

// #region doc-links
// Links in a served document lead out of the app (GitHub, a diagram page), so
// they open in a new tab and the dialog stays where the reader was. The docs
// name the live domain in full, which keeps them right on GitHub; here the same
// links are made relative, so a checkout on localhost opens its own diagram
// page and not the live one (ADR-020).
const SITE = 'https://theyard.stevenstout.biz/';
marked.use({
  hooks: {
    postprocess(html: string) {
      return html
        .replaceAll(`href="${SITE}`, 'href="/')
        .replace(/<a href="(?!#)/g, '<a target="_blank" rel="noopener" href="');
    },
  },
});
// #endregion doc-links

// #region doc-tables
// Every markdown table in a scroller of its own (the tweaks pass, A1), reachable
// from the keyboard, so a phone scrolls a wide table and never breaks a word. A
// renderer and not a string replace (the self-review of 25 September): a raw
// HTML table in a document is left as its author wrote it, where the replace
// wrapped its end and not its start. A region, like every table wrapper on the
// Admin tab.
const plainRenderer = new marked.Renderer();
marked.use({
  renderer: {
    table(token) {
      return `<div class="table-scroll" role="region" aria-label="Table" tabindex="0">${plainRenderer.table.call(this, token).trimEnd()}</div>\n`;
    },
  },
});
// #endregion doc-tables

// #region doc-images
// A document's pictures load when the reader reaches them (1.0.3.5): the
// README carries a screenshot and a drawing below its first screen, and the
// text should not be sharing the connection with them. The address they carry
// is this site's own since the same version (DocumentationCatalog.cs, DocImages).
marked.use({
  renderer: {
    image({ href, title, text }) {
      const alt = text.replace(/"/g, '&quot;');
      const caption = title ? ` title="${title.replace(/"/g, '&quot;')}"` : '';
      // The served address carries the picture's size after a hash (DocImages.Rewrite): it becomes the
      // width and height, so the room is held before the picture arrives and nothing under it moves.
      const sized = /^(.*)#(\d+)x(\d+)$/.exec(href);
      const src = sized ? sized[1] : href;
      const size = sized ? ` width="${sized[2]}" height="${sized[3]}"` : '';
      return `<img src="${src}" alt="${alt}"${caption}${size} loading="lazy" decoding="async">`;
    },
  },
});
// #endregion doc-images

// #region code-renderer
// Every fenced block in a served document goes through the highlighter
// (ADR: Code that reads like code). marked hands back the code and the name on
// the fence; the name is checked against the grammars this bundle carries
// before it reaches a class attribute, so a fence cannot write markup of its
// own, and the highlighter escapes everything it does not tokenize.
marked.use({
  renderer: {
    code({ text, lang }) {
      const name = (lang ?? '').trim().split(/\s+/)[0];
      // A `swatches` fence is not code: it is a list of tokens, drawn as a
      // sheet of swatches from the token sheet itself (ADR-016, the addendum on
      // the style section).
      if (name === 'swatches') return swatchSheet(text, tokenSheet);
      // The Style section's tiles, readouts, glossary and ribbon strip (src/library/styleBlocks.ts).
      if ((STYLE_FENCES as readonly string[]).includes(name))
        return styleBlock(name as StyleFence, text);
      const grammar = grammarFor(name);
      const className = grammar ? `hljs language-${grammar}` : 'hljs';
      return `<pre><code class="${className}">${highlight(text, name)}</code></pre>\n`;
    },
  },
});
// #endregion code-renderer

// #region heading-ids
/**
 * Every second-level heading gets an id from its words, the way GitHub makes
 * one, so a link can land on a section (`?doc=color-style#the-glass`): the
 * Style guide's glossary links to the section each term is used in. Two
 * headings with the same words are told apart by a number, as GitHub does.
 */
export function withHeadingIds(html: string): string {
  const used = new Map<string, number>();
  return html.replace(/<h2>([\s\S]*?)<\/h2>/g, (_, inner: string) => {
    const base =
      inner
        .replace(/<[^>]+>/g, '')
        .replace(/&[a-z]+;|&#\d+;/g, '')
        .toLowerCase()
        .replace(/[^a-z0-9 -]/g, '')
        .trim()
        .replace(/ +/g, '-') || 'section';
    const seen = used.get(base) ?? 0;
    used.set(base, seen + 1);
    return `<h2 id="${seen === 0 ? base : `${base}-${seen}`}">${inner}</h2>`;
  });
}
// #endregion heading-ids

/** Our own docs, trusted, repository-authored content, rendered whole. */
export async function renderDocument(markdown: string): Promise<string> {
  return withHeadingIds(await marked.parse(markdown, { async: true }));
}
