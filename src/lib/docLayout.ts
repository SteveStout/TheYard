/**
 * Every other document's shape, on the Author page's panels.
 *
 * Until now the Author page was the only document with a shape: its words sat
 * on glass panels with a green edge, and every other document, the decision
 * records among them, was a single column of markdown on the dialog's white.
 * The owner's rule: "our documentation isn't formatted like the author
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

// #region layout-document
const PANEL = 'doc-panel';

/** Splits at every second-level heading that starts a line of markup, never inside a code block. */
const AT_H2 = /(?=<h2[ >])/;

/**
 * The rendered document as panels. `lead` carries the title and any words
 * before the first heading; each heading after it opens a panel of its own.
 */
export function layoutDocument(html: string, title?: string): string {
  const { hidden, rest } = sayTitleOnce(html, title);
  const parts = rest.split(AT_H2);
  const lead = /^<h2[ >]/.test(parts[0]) ? '' : parts[0];
  const sections = lead === '' ? parts : parts.slice(1);
  // A lead that held only the title leaves no panel of its own: the title, unseen, opens the first panel instead.
  const first = lead.trim() === '' && sections.length > 0 ? hidden : '';
  const panels = [
    ...(lead.trim() === ''
      ? hidden !== '' && sections.length === 0
        ? [`<section class="${PANEL} op-glass doc-lead">${hidden}</section>`]
        : []
      : [`<section class="${PANEL} op-glass doc-lead">${hidden}${statusReading(lead)}</section>`]),
    ...sections.map(
      (section, i) =>
        `<section class="${PANEL} op-glass">${i === 0 ? first : ''}${section}</section>`
    ),
  ];
  return `<div class="doc-page" data-testid="doc-page">${panels.join('')}</div>`;
}

/**
 * One title (the quality1120 picture): the window's bar already says the document's name, so a first heading that
 * says the same words is not drawn a second time under it. It stays in the page, unseen, so a screen reader still
 * meets the document's heading. A first heading with words of its own is drawn as it was.
 */
export function sayTitleOnce(html: string, title?: string): { hidden: string; rest: string } {
  const first = /^\s*<h1([^>]*)>([\s\S]*?)<\/h1>\s*/.exec(html);
  if (!first || title === undefined) return { hidden: '', rest: html };
  const words = first[2]
    .replace(/<[^>]+>/g, '')
    .replace(/&amp;/g, '&')
    .trim();
  if (words !== title.trim()) return { hidden: '', rest: html };
  return {
    hidden: `<h1${first[1]} class="doc-title-in-bar">${first[2]}</h1>`,
    rest: html.slice(first[0].length),
  };
}
// #endregion layout-document

// #region status-reading
/**
 * A record's status line as a reading (the operator's look): "Status: accepted,
 * 2026-09-02, shipped as 1.0.0.21." opens most decision records, and it is a
 * reading, not prose, so it is drawn as one, a label and its value for each of
 * the three, in small spaced capitals. Whatever the paragraph says after that
 * first sentence stays a paragraph, word for word. A status line in any other
 * shape is left as the writer wrote it.
 */
const STATUS =
  /<p>Status: ([a-z]+), (\d{4}-\d{2}-\d{2})(?:, shipped as (\d+(?:\.\d+){2,3}))?\.\s*([\s\S]*?)<\/p>/;

export function statusReading(lead: string): string {
  const match = STATUS.exec(lead);
  if (!match) return lead;
  const [whole, status, date, shipped, rest] = match;
  const rows: [string, string][] = [
    ['Status', status],
    ['Date', date],
    ...(shipped ? [['Shipped as', shipped] as [string, string]] : []),
  ];
  const reading =
    `<dl class="doc-status">` +
    rows.map(([label, value]) => `<div><dt>${label}</dt><dd>${value}</dd></div>`).join('') +
    `</dl>`;
  return lead.replace(whole, reading + (rest.trim() === '' ? '' : `<p>${rest}</p>`));
}
// #endregion status-reading
