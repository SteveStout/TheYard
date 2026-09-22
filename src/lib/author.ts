/**
 * The Author page's layout (ADR: The sidebar, the addendum on the author's
 * section). The words are a served document
 * like every other, docs/AUTHOR.md, and they go through the same renderer.
 * What a letter-shaped document cannot give is the page's shape: a panel for
 * who he is with three ways to reach him, a panel of small headed blocks for
 * the rest of his life, photographs in one quiet frame, a closing line. This
 * takes the renderer's HTML and gives it that shape. Plain string work over
 * markup this project's own renderer wrote from this project's own document,
 * and no React.
 *
 * What it relies on in the document, and nothing else: second-level headings
 * open the panels, third-level headings open the blocks, a list whose every
 * item is one link is a row of buttons, an image that names one of the
 * page's photographs is that photograph, a rule opens the closing panel, and
 * a second rule opens the small print under the panels.
 */
import { AUTHOR_PHOTOS, photoFigure, photoNamed } from './authorPhotos';

// #region buttons
/** A list in which every item is a single link, drawn as a row of buttons with finger-sized targets. */
function buttons(html: string): string {
  return html.replace(
    /<ul>\s*((?:<li><a [^>]*>[^<]*<\/a><\/li>\s*)+)<\/ul>/g,
    (_list, items: string) => {
      const links = Array.from(items.matchAll(/<li>(<a [^>]*>[^<]*<\/a>)<\/li>/g), (item) =>
        item[1].replace('<a ', '<a class="author-button" ')
      );
      return `<p class="author-buttons">${links.join('')}</p>`;
    }
  );
}
// #endregion buttons

// #region photographs
/**
 * An image the document names becomes the photograph's responsive figure, its
 * alt text the document's and its caption the image's title. An image that is
 * not one of the page's photographs is dropped, and says so in the markup: a
 * picture of a person does not reach this page by being typed into a document.
 */
function photographs(html: string): string {
  return html.replace(
    /(?:<p>)?<img src="([^"]*)" alt="([^"]*)"(?: title="([^"]*)")?\s*\/?>(?:<\/p>)?/g,
    (_image, address: string, alt: string, title: string | undefined) => {
      const photo = photoNamed(address);
      if (photo === null) return '<!-- a picture this page does not serve was left out -->';
      const unescape = (text: string) =>
        text
          .replace(/&quot;/g, '"')
          .replace(/&#39;/g, "'")
          .replace(/&lt;/g, '<')
          .replace(/&gt;/g, '>')
          .replace(/&amp;/g, '&');
      // Every photograph on this page loads eagerly (Steve, 2026-09-22, iPhone Chrome and
      // Safari: the lazy ones flickered while the page scrolled, and the first, the only eager
      // one, never did; nor do the vehicle photographs, which scroll the page, not a scroller
      // inside a dialog). The page is a dozen cuts no wider than 960 on a phone.
      const figure = photoFigure(
        photo,
        unescape(alt),
        title === undefined ? null : unescape(title),
        true
      );
      return figure;
    }
  );
}
// #endregion photographs

// #region panels
const FIGURE = /<figure class="author-photo"[\s\S]*?<\/figure>/g;

/** The photographs marked `beneath` in the list: never paired, never cut, under the one before. */
const BENEATH = new Set(AUTHOR_PHOTOS.filter((photo) => photo.beneath).map((photo) => photo.name));
const nameOf = (figure: string) => /data-photo="([^"]+)"/.exec(figure)?.[1] ?? '';

/**
 * Two photographs with nothing between them sit side by side, and one under
 * the other on a phone, unless either is marked `beneath`: that one stays
 * under the photograph before it, in its own shape (1.0.1.4).
 */
function pairs(html: string): string {
  return html
    .replace(
      /(<figure class="author-photo"[\s\S]*?<\/figure>)\s*(<figure class="author-photo"[\s\S]*?<\/figure>)/g,
      (both: string, first: string, second: string) =>
        BENEATH.has(nameOf(first)) || BENEATH.has(nameOf(second))
          ? both
          : `<div class="author-pair">${first}${second}</div>`
    )
    .replace(/<figure class="author-photo" data-photo="([^"]+)">/g, (figure, name: string) =>
      BENEATH.has(name)
        ? `<figure class="author-photo author-beneath" data-photo="${name}">`
        : figure
    );
}

/** The photographs marked `between` in the list: each stands alone, full width, after the block it closes. */
const BETWEEN = new Set(AUTHOR_PHOTOS.filter((photo) => photo.between).map((photo) => photo.name));

function standAlone(block: string): [string, string[]] {
  const between: string[] = [];
  const kept = block.replace(FIGURE, (figure) => {
    const name = /data-photo="([^"]+)"/.exec(figure)?.[1] ?? '';
    if (!BETWEEN.has(name)) return figure;
    between.push(figure);
    return '';
  });
  return [kept, between];
}

/**
 * Everything from one third-level heading to the next, as a block of its own.
 * A block that holds photographs takes the whole row, and so does the last
 * plain one when the plain ones are an odd number, so no block ever sits
 * beside a hole.
 */
function blocks(section: string): string {
  const parts = section.split(/(?=<h3[ >])/);
  if (parts.length === 1) return pairs(section);
  const [lead, ...headed] = parts;
  const pictured = headed.map((block) => standAlone(block)[0].includes('class="author-photo"'));
  const plain = pictured.filter((has) => !has).length;
  const lastPlain = pictured.lastIndexOf(false);
  // Two neighbouring blocks with one photograph each, and no photograph standing between
  // them, share a row as halves, their photographs cut to one size (Steve, 2026-09-22:
  // the lake beside History, the food beside the doors).
  const photos = headed.map((block) => (standAlone(block)[0].match(FIGURE) ?? []).length);
  const betweenAfter = headed.map((block) => standAlone(block)[1].length > 0);
  const half = headed.map(() => false);
  for (let index = 0; index + 1 < headed.length; index++) {
    if (half[index]) continue;
    if (photos[index] === 1 && photos[index + 1] === 1 && !betweenAfter[index]) {
      half[index] = true;
      half[index + 1] = true;
      index++;
    }
  }
  return (
    pairs(lead) +
    `<div class="author-blocks">` +
    headed
      .map((block, index) => {
        const wide = !half[index] && (pictured[index] || (plain % 2 === 1 && index === lastPlain));
        const [kept, between] = standAlone(block);
        const shape = half[index] ? ' author-block-half' : wide ? ' author-block-wide' : '';
        return (
          `<section class="author-block${shape}">${pairs(kept)}</section>` +
          between.map((figure) => `<div class="author-between">${figure}</div>`).join('')
        );
      })
      .join('') +
    `</div>`
  );
}

/** The first panel: the words on one side and its photograph, when it has one, on the other. */
function intro(section: string): string {
  const figures = section.match(FIGURE);
  if (figures === null) return section;
  return (
    `<div class="author-hero"><div class="author-hero-words">${section.replace(FIGURE, '')}</div>` +
    `${figures[0]}</div>`
  );
}

/**
 * The page in its shape: the first panel is who he is, every later one is
 * split into its blocks, and what follows a rule is the closing panel. The
 * blocks carry no colour of their own: the stylesheet alternates them by
 * their order, so an edit to the document cannot leave two the same.
 */
export function layoutAuthor(html: string): string {
  const shaped = photographs(buttons(html));
  const [body, ...closing] = shaped.split(/<hr\s*\/?>/);
  const parts = body.split(/(?=<h2[ >])/);
  // Whatever comes before the first panel's heading, the page's own title as a rule, stays above the panels.
  const top = /^<h2[ >]/.test(parts[0]) ? '' : parts[0];
  const sections = top === '' ? parts : parts.slice(1);
  const panels = sections.map((section, index) =>
    index === 0
      ? `<section class="author-panel author-intro">${intro(section)}</section>`
      : `<section class="author-panel">${blocks(section)}</section>`
  );
  const close =
    closing.length === 0
      ? ''
      : `<section class="author-panel author-close">${closing[0]}</section>`;
  // A second rule opens the small print under the panels: the photographer's credit.
  const credit =
    closing.length < 2 ? '' : `<footer class="author-credit">${closing.slice(1).join('')}</footer>`;
  return `<div class="author-page" data-testid="author-page">${top}${panels.join('')}${close}${credit}</div>`;
}
// #endregion panels
