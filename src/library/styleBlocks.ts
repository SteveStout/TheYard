/**
 * Does:      Turns the Style section's three fences into markup: `tiles` (a sub-page each, in the landing tile's
 *            look), `readouts` (the numbers the build holds) and `glossary` (a term, its CSS, what it means, where
 *            it is used), plus the `ribbon-strip` placeholder the live ribbons are drawn into.
 * Does not:  Count anything (the API writes every number into the fence before it arrives, LiveCounts.cs), draw
 *            the ribbons or a tile's icon (useLiveBlocks.tsx mounts them), or hold a colour of its own.
 * Used by:   markdown.ts, styleBlocks.test.ts.
 */

// #region style-blocks
/** The fences this file draws, by the name on the fence. */
export const STYLE_FENCES = ['tiles', 'readouts', 'glossary', 'ribbon-strip'] as const;
export type StyleFence = (typeof STYLE_FENCES)[number];

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** A fence's lines split on `|`, blank lines and lines with too few cells dropped. */
function cells(fence: string, least: number): string[][] {
  return fence
    .split('\n')
    .map((line) => line.split('|').map((cell) => cell.trim()))
    .filter((row) => row.length >= least && row[0] !== '');
}

/** An in-library link: a document's slug and, after a hash, the section it lands on. */
function docLink(target: string, label: string, className: string): string {
  const [slug, anchor = ''] = target.split('#');
  const href = `/?doc=${encodeURIComponent(slug)}${anchor ? `#${encodeURIComponent(anchor)}` : ''}`;
  return `<a class="${className}" href="${href}" data-doc="${escape(slug)}" data-anchor="${escape(anchor)}">${label}</a>`;
}

/** `slug | title | one line | icon`: a tile per sub-page, its icon mounted from the site map's glyphs. */
export function tileSheet(fence: string): string {
  const tiles = cells(fence, 4).map(
    ([slug, title, blurb, icon]) =>
      `<li>${docLink(
        slug,
        `<span class="style-tile-badge" data-glyph="${escape(icon)}"></span><span class="style-tile-text"><strong>${escape(title)}</strong><span>${escape(blurb)}</span></span>`,
        'style-tile op-glass op-tile op-card'
      )}</li>`
  );
  return `<ul class="style-tiles" data-testid="style-tiles">${tiles.join('')}</ul>\n`;
}

/** `label | value`: one stat tile each. The value arrives as the API counted it. */
export function readoutSheet(fence: string): string {
  const readouts = cells(fence, 2).map(
    ([label, value]) =>
      `<div class="style-readout-tile op-glass op-tile op-card"><dt>${escape(label)}</dt><dd data-testid="style-readout">${escape(value)}</dd></div>`
  );
  return `<dl class="style-readouts">${readouts.join('')}</dl>\n`;
}

/** `term | syntax | what it means | slug#section | page title`: a definition list that links to where each term is used. */
export function glossarySheet(fence: string): string {
  const entries = cells(fence, 5).map(([term, syntax, meaning, target, page]) => {
    const code = syntax ? ` <code>${escape(syntax)}</code>` : '';
    return `<div class="style-glossary-entry"><dt>${escape(term)}${code}</dt><dd>${escape(meaning)} ${docLink(target, `See ${escape(page)}`, 'style-glossary-see')}</dd></div>`;
  });
  return `<dl class="style-glossary" data-testid="style-glossary">${entries.join('')}</dl>\n`;
}

/** The fence's lines are its caption; the ribbons themselves are mounted by useLiveBlocks.tsx. */
export function ribbonStrip(fence: string): string {
  const caption = fence.trim();
  return `<figure class="style-ribbon-strip"><div class="style-ribbon-box" data-ribbon-strip data-testid="ribbon-strip"></div>${caption ? `<figcaption>${escape(caption)}</figcaption>` : ''}</figure>\n`;
}

/** The markup for one of the fences above. */
export function styleBlock(name: StyleFence, fence: string): string {
  switch (name) {
    case 'tiles':
      return tileSheet(fence);
    case 'readouts':
      return readoutSheet(fence);
    case 'glossary':
      return glossarySheet(fence);
    case 'ribbon-strip':
      return ribbonStrip(fence);
  }
}
// #endregion style-blocks
