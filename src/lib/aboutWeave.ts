/**
 * Does:      Sets How I lead and Away from the keyboard out as the About page draws them (ADR: The sidebar, the addendum on
 *            the person beside the work): three of the document's own stories inside the lead cards they sit with, under
 *            the away panel's own heading as their label; the team drawing in the coaching card; every other away block
 *            an even card in the page's order with its photographs first; the couple's photographs in one strip
 *            with their credit. Plain string work over the panels author.ts returns, keyed by the document's own
 *            third-level headings, and nothing is ever dropped: a key that matches nothing leaves its block where it was.
 * Does not:  Change the document or its order (/about and every other reader keep it), read it (useAuthorParts.ts does),
 *            or size a picture (AboutPage.module.css does); no React in here.
 * Used by:   AboutPage.tsx, aboutWeave.test.ts.
 */
import { AUTHOR_PHOTOS } from './authorPhotos';

// #region keys
/** Each lead card, by its title, with the away story that sits inside it, by its title. */
export const PAIRS: readonly (readonly [card: string, story: string])[] = [
  ['Decisions written down', 'The workshop'],
  ['Tests that guard the business', 'The doors'],
  ['Tradeoffs in the open', 'The lake, and no boat'],
];

/** The lead card that takes the team drawing, the picture between How I lead's heading and its lede. */
export const COACH = 'Coaching inside the system';

/** The away cards in the page's order; a block this does not name is drawn after the named ones. */
export const AWAY_ORDER: readonly string[] = [
  'History',
  'St. Charles',
  'Food',
  'Games',
  'Modding',
  'Too many house plants',
  'Freya and Vincent',
];

/** The photograph standing between blocks that joins the rabbits' card as its last photo. */
export const RABBITS = { card: 'Freya and Vincent', photo: 'rabbits-under-hay-rack' } as const;
// #endregion keys

/** One photograph or drawing as the document's figure, and the widest box its largest file fills at 2x, when that binds. */
export type Photo = { figure: string; cap: number | null };

/** A personal story inside a lead card: its title as words, its words, its pictures. */
export type Aside = { title: string; body: string; figures: string[] };

/** A lead card: its heading and words, the story beside it, and the team drawing when it is the coaching card. */
export type LeadCard = {
  title: string;
  heading: string;
  body: string;
  aside: Aside | null;
  picture: string | null;
};

/** How I lead: its heading, its lede, the four cards, and the team drawing when no card took it. */
export type Lead = { heading: string; lede: string; cards: LeadCard[]; picture: string | null };

/** An away card: its heading and words, its photographs first, and whether it runs the full width. */
export type AwayCard = {
  title: string;
  heading: string;
  body: string;
  photos: Photo[];
  wide: boolean;
};

/** Away from the keyboard: its heading, its words as the label, the cards, and the strip with its credit. */
export type Away = {
  heading: string;
  label: string;
  cards: AwayCard[];
  strip: { photos: Photo[]; credit: string | null } | null;
};

/** The page's two woven panels, the panels after them as the document has them, and the credit when no strip holds it. */
export type Woven = { lead: Lead | null; away: Away | null; more: string[]; credit: string | null };

// #region reading
const FIGURE = /<figure class="author-(?:photo|drawing)"[\s\S]*?<\/figure>/g;
const PAIR = /<div class="author-pair">([\s\S]*?)<\/div>/g;
const BLOCKS = '<div class="author-blocks">';
const ITEM =
  /<section class="author-block[^"]*">([\s\S]*?)<\/section>|<div class="author-between">([\s\S]*?)<\/div>/g;

/** A heading's words, as a reader sees them. */
export function words(html: string): string {
  return html
    .replace(/<[^>]+>/g, '')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&')
    .trim();
}

/** A figure's name: the photograph's data-photo, or the drawing's file stem. */
export function nameOf(figure: string): string {
  return (
    /data-photo="([^"]+)"/.exec(figure)?.[1] ??
    /src="[^"]*\/([a-z0-9-]+)\.svg"/.exec(figure)?.[1] ??
    ''
  );
}

type Block = { title: string; heading: string; body: string; figures: string[] };

/** A block of the document: its heading, its pictures in order, and the words left when both are out. */
function blockOf(inside: string, tag: 'h2' | 'h3'): Block {
  const flat = inside.replace(PAIR, '$1');
  const heading = new RegExp(`<${tag}[^>]*>[\\s\\S]*?</${tag}>`).exec(flat)?.[0] ?? '';
  return {
    title: words(heading),
    heading,
    body: flat.replace(heading, '').replace(FIGURE, '').trim(),
    figures: flat.match(FIGURE) ?? [],
  };
}

/** A panel as author.ts wrote it: what comes before its blocks, its blocks, and the figures that stand between them. */
function panelOf(html: string): { head: Block; blocks: Block[]; between: string[] } {
  const at = html.indexOf(BLOCKS);
  const head = blockOf(at < 0 ? html : html.slice(0, at), 'h2');
  const inside = at < 0 ? '' : html.slice(at + BLOCKS.length, html.lastIndexOf('</div>'));
  const blocks: Block[] = [];
  const between: string[] = [];
  for (const item of inside.matchAll(ITEM)) {
    if (item[1] !== undefined) blocks.push(blockOf(item[1], 'h3'));
    else between.push(...(item[2].match(FIGURE) ?? []));
  }
  return { head, blocks, between };
}
// #endregion reading

/**
 * The widest box, in CSS pixels, a photograph's largest file fills at 2x in a 4:3 cut, with the image gate's 5 per cent
 * allowance; null when it is 420 or more, which no card on the page reaches. The Pantheon's 678 x 452 gives 316.
 */
export function capOf(name: string): number | null {
  const photo = AUTHOR_PHOTOS.find((one) => one.name === name);
  if (photo === undefined) return null;
  const width = photo.widths[photo.widths.length - 1];
  const height = width * photo.ratio;
  const cap = Math.floor(Math.min(width / 2, ((height / 2) * 4) / 3) * 1.05);
  return cap < 420 ? cap : null;
}

const photosOf = (figures: string[]): Photo[] =>
  figures.map((figure) => ({ figure, cap: capOf(nameOf(figure)) }));

/** The lead panel's cards, each with the story the pairs give it, taken out of the away blocks it came from. */
function leadOf(html: string, stories: Block[]): Lead {
  const { head, blocks } = panelOf(html);
  const cards: LeadCard[] = blocks.map((card) => {
    const pair = PAIRS.find(([title]) => title === card.title);
    const at = pair === undefined ? -1 : stories.findIndex((story) => story.title === pair[1]);
    const [story] = at < 0 ? [] : stories.splice(at, 1);
    return {
      title: card.title,
      heading: card.heading,
      body: [card.body, ...card.figures].join(''),
      aside:
        story === undefined
          ? null
          : { title: story.title, body: story.body, figures: story.figures },
      picture: null,
    };
  });
  const drawing = head.figures.length === 0 ? null : head.figures.join('');
  const coach = cards.findIndex((card) => card.title === COACH);
  if (drawing !== null && coach >= 0) {
    // The coaching card holds the drawing and runs the full width, so it is the last of the four.
    const [card] = cards.splice(coach, 1);
    cards.push({ ...card, picture: drawing });
  }
  return {
    heading: head.heading,
    lede: head.body,
    cards,
    picture: drawing !== null && coach < 0 ? drawing : null,
  };
}

/** The away cards in the page's order, the hay-rack photograph with the rabbits, and the strip from the rest. */
function awayOf(head: Block, blocks: Block[], between: string[], credit: string | null): Away {
  const named = AWAY_ORDER.map((title) => blocks.find((block) => block.title === title)).filter(
    (block): block is Block => block !== undefined
  );
  const ordered = [...named, ...blocks.filter((block) => !named.includes(block))];
  const rabbits = ordered.find((block) => block.title === RABBITS.card);
  const strip: string[] = [];
  const joined: string[] = [];
  for (const figure of between) {
    const name = nameOf(figure);
    if (name === RABBITS.photo && rabbits !== undefined) joined.push(figure);
    // The couple's photographs (couple-*), in the document's order, and any other that stands between
    // blocks with no card to join, so none is ever left out.
    else strip.push(figure);
  }
  const cards = ordered.map((block) => {
    const figures = block === rabbits ? [...block.figures, ...joined] : block.figures;
    return {
      title: block.title,
      heading: block.heading,
      body: block.body,
      photos: photosOf(figures),
      wide: figures.length >= 3,
    };
  });
  return {
    heading: head.heading,
    label: head.title,
    cards,
    strip:
      strip.length === 0
        ? null
        : { photos: strip.map((figure) => ({ figure, cap: null })), credit },
  };
}

/**
 * The About page's How I lead and Away from the keyboard, woven from the document's panels: the first is How I lead,
 * the second Away from the keyboard, any later one is drawn as the document has it.
 * @param panels the later panels as authorParts gives them
 * @param credit the document's small print, drawn in the strip when there is one
 */
export function weave(panels: readonly string[], credit: string | null): Woven {
  const [leadHtml, awayHtml, ...more] = panels;
  const away = awayHtml === undefined ? null : panelOf(awayHtml);
  const stories = away === null ? [] : [...away.blocks];
  const lead = leadHtml === undefined ? null : leadOf(leadHtml, stories);
  const woven = away === null ? null : awayOf(away.head, stories, away.between, credit);
  return {
    lead,
    away: woven,
    more,
    credit: woven?.strip ? null : credit,
  };
}
