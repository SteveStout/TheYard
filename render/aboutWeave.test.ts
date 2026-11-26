import { beforeAll, describe, expect, it } from 'vitest';
import document from '../docs/author/AUTHOR.md?raw';
import { authorParts, type AuthorParts } from '../src/lib/author';
import { AUTHOR_PHOTOS } from '../src/lib/authorPhotos';
import {
  AWAY_ORDER,
  capOf,
  COACH,
  nameOf,
  PAIRS,
  RABBITS,
  weave,
  type Woven,
} from '../src/lib/aboutWeave';
import { renderDocument } from '../src/lib/markdown';

/**
 * The weave against the real docs/author/AUTHOR.md. It lives beside about.test.ts, which reads the document the same
 * way, because a test under src may import nothing the image's build stage does not copy (DockerBuildInputsTests).
 */

/** Every figure's name in a piece of markup, in order. */
const names = (html: string) =>
  Array.from(html.matchAll(/<figure[\s\S]*?<\/figure>/g), (figure) => nameOf(figure[0]));

/** Every third-level heading's words in a piece of markup, in order. */
const titles = (html: string) =>
  Array.from(html.matchAll(/<h3[^>]*>([\s\S]*?)<\/h3>/g), (heading) => heading[1].trim());

/** Every block title and every figure the woven page draws, wherever it put them. */
function drawn(woven: Woven) {
  const lead = woven.lead?.cards ?? [];
  const away = woven.away?.cards ?? [];
  return {
    titles: [
      ...lead.map((card) => card.title),
      ...lead.flatMap((card) => (card.aside ? [card.aside.title] : [])),
      ...away.map((card) => card.title),
    ],
    figures: [
      ...names(woven.lead?.picture ?? ''),
      ...lead.flatMap((card) => [
        ...names(card.body),
        ...names(card.picture ?? ''),
        ...(card.aside?.figures.map(nameOf) ?? []),
      ]),
      ...away.flatMap((card) => card.photos.map((photo) => nameOf(photo.figure))),
      ...(woven.away?.strip?.photos.map((photo) => nameOf(photo.figure)) ?? []),
    ],
  };
}

describe('the About page woven from the real author document', () => {
  let parts: AuthorParts;
  let woven: Woven;
  beforeAll(async () => {
    parts = authorParts(await renderDocument(document));
    woven = weave(parts.panels, parts.credit);
  });

  it('finds every key it is written against, so none is silently ignored', () => {
    const lead = titles(parts.panels[0]);
    const away = titles(parts.panels[1]);
    for (const [card, story] of PAIRS) {
      expect(lead).toContain(card);
      expect(away).toContain(story);
    }
    expect(lead).toContain(COACH);
    for (const title of AWAY_ORDER) expect(away).toContain(title);
    expect(names(parts.panels[1])).toContain(RABBITS.photo);
  });

  it('sets each story inside the lead card it sits with, and the team drawing in the coaching card, last', () => {
    const cards = woven.lead!.cards;
    expect(cards.map((card) => card.title)).toEqual([
      'Decisions written down',
      'Tests that guard the business',
      'Tradeoffs in the open',
      'Coaching inside the system',
    ]);
    expect(cards.map((card) => card.aside?.title ?? null)).toEqual([
      'The workshop',
      'The doors',
      'The lake, and no boat',
      null,
    ]);
    expect(cards[0].aside!.figures.map(nameOf)).toEqual(['about-birdhouse']);
    expect(cards[1].aside!.body).toContain('Katie was very happy when they went back on.');
    expect(cards[3].picture).toContain('data-photo="how-i-lead"');
    expect(woven.lead!.picture).toBeNull();
    expect(woven.lead!.heading).toContain('How I lead');
    expect(woven.lead!.lede).toContain('then coach people inside them.');
  });

  it("reads the stories' label from the away panel's own heading", () => {
    expect(woven.away!.label).toBe('Away from the keyboard');
  });

  it('draws the away cards in the page order, the hay-rack photo with the rabbits, and the strip with its credit', () => {
    const away = woven.away!;
    expect(away.cards.map((card) => card.title)).toEqual(AWAY_ORDER);
    expect(away.cards.map((card) => card.photos.length)).toEqual([1, 1, 1, 1, 2, 2, 3]);
    expect(nameOf(away.cards[6].photos[2].figure)).toBe(RABBITS.photo);
    expect(away.cards.filter((card) => card.wide).map((card) => card.title)).toEqual([
      'Freya and Vincent',
    ]);
    expect(away.strip!.photos.map((photo) => nameOf(photo.figure))).toEqual([
      'couple-crossing-stream',
      'couple-on-wooden-bridge-wide',
      'couple-under-willow',
    ]);
    expect(away.strip!.credit).toContain('by McKinley Griggs.');
    expect(woven.credit).toBeNull();
    expect(woven.more).toEqual([]);
  });

  it('draws every block and every picture of the two panels exactly once', () => {
    const { titles: blocks, figures } = drawn(woven);
    expect([...blocks].sort()).toEqual(
      [...titles(parts.panels[0]), ...titles(parts.panels[1])].sort()
    );
    expect([...figures].sort()).toEqual(
      [...names(parts.panels[0]), ...names(parts.panels[1])].sort()
    );
    // With the hero's photograph, the page's nineteen pictures.
    expect(figures).toHaveLength(18);
  });

  it('caps the Pantheon at 316 px, the box its 678 x 452 file fills at 2x, and no other photograph', () => {
    expect(capOf('pantheon-at-dusk')).toBe(316);
    const capped = AUTHOR_PHOTOS.filter((photo) => capOf(photo.name) !== null);
    expect(capped.map((photo) => photo.name)).toEqual(['pantheon-at-dusk']);
    expect(capOf('no-such-photo')).toBeNull();
    const history = woven.away!.cards[0];
    expect(history.photos[0].cap).toBe(316);
  });
});
