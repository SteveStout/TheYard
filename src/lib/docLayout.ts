/**
 * Every other document's shape (1.0.2.0), on the Author page's panels.
 *
 * Until now the Author page was the only document with a shape: its words sat
 * on glass panels with a green edge, and every other document, the decision
 * records among them, was a single column of markdown on the dialog's white.
 * Steve, 2026-09-22: "our documentation isn't formatted like the author
 * section with the nice background and formatting". So the same panels are
 * given to all of them, by the one thing every document in this repository
 * has: second-level headings.
 *
 * What it relies on, and nothing else: the title and whatever comes before the
 * first second-level heading open the page, and each second-level heading
 * starts a panel that runs to the next one. A document with no second-level
 * heading is one panel. Plain string work over the markup this project's own
 * renderer wrote from this project's own documents, and no React.
 */

const PANEL = 'doc-panel';

/** Splits at every second-level heading that starts a line of markup, never inside a code block. */
const AT_H2 = /(?=<h2[ >])/;

/**
 * The rendered document as panels. `lead` carries the title and any words
 * before the first heading; each heading after it opens a panel of its own.
 */
export function layoutDocument(html: string): string {
  const parts = html.split(AT_H2);
  const lead = /^<h2[ >]/.test(parts[0]) ? '' : parts[0];
  const sections = lead === '' ? parts : parts.slice(1);
  const panels = [
    ...(lead.trim() === '' ? [] : [`<section class="${PANEL} doc-lead">${lead}</section>`]),
    ...sections.map((section) => `<section class="${PANEL}">${section}</section>`),
  ];
  return `<div class="doc-page" data-testid="doc-page">${panels.join('')}</div>`;
}
