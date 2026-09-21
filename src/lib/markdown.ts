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
import tokenSheet from '../styles/tokens.css?raw';

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
      const grammar = grammarFor(name);
      const className = grammar ? `hljs language-${grammar}` : 'hljs';
      return `<pre><code class="${className}">${highlight(text, name)}</code></pre>\n`;
    },
  },
});
// #endregion code-renderer

/** Our own docs, trusted, repository-authored content, rendered whole. */
export function renderDocument(markdown: string): Promise<string> {
  return marked.parse(markdown, { async: true });
}
