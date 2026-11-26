import { describe, expect, it } from 'vitest';
import { COACH, nameOf, RABBITS, weave } from './aboutWeave';

describe('the weave when the document changes under its keys', () => {
  const figure = (name: string) =>
    `<figure class="author-photo" data-photo="${name}"><img></figure>`;
  const lead = (cards: string[], drawing = true) =>
    `<h2 id="lead">How I lead</h2>${drawing ? figure('how-i-lead') : ''}<p><strong>Lede.</strong></p>` +
    `<div class="author-blocks">${cards
      .map(
        (title) =>
          `<section class="author-block op-glass"><h3>${title}</h3><p>${title} words.</p></section>`
      )
      .join('')}</div>`;
  const away = (heading: string, blocks: string[], between: string[] = []) =>
    `<h2 id="away">${heading}</h2><div class="author-blocks">${blocks
      .map(
        (title) =>
          `<section class="author-block op-glass"><h3>${title}</h3><p>${title} story.</p>` +
          `<div class="author-pair">${figure(`${title.toLowerCase().replace(/\W+/g, '-')}-a`)}${figure('second')}</div></section>`
      )
      .join(
        ''
      )}${between.map((name) => `<div class="author-between">${figure(name)}</div>`).join('')}</div>`;

  it('leaves a story whose card is gone in the away cards, after the named ones', () => {
    const woven = weave(
      [
        lead(['Tests that guard the business']),
        away('Away', ['The doors', 'The workshop', 'History']),
      ],
      null
    );
    expect(woven.lead!.cards[0].aside!.title).toBe('The doors');
    expect(woven.away!.cards.map((card) => card.title)).toEqual(['History', 'The workshop']);
  });

  it('draws a card whose story is gone with no aside, and a block the order does not name after the named ones', () => {
    const woven = weave(
      [lead(['Decisions written down', 'A new practice']), away('Away', ['Gardening', 'Food'])],
      null
    );
    expect(woven.lead!.cards.map((card) => card.aside)).toEqual([null, null]);
    expect(woven.away!.cards.map((card) => card.title)).toEqual(['Food', 'Gardening']);
  });

  it("follows the away panel's heading when its words change", () => {
    const woven = weave(
      [lead(['Tradeoffs in the open']), away('Off the clock', ['The lake, and no boat'])],
      null
    );
    expect(woven.away!.label).toBe('Off the clock');
    expect(woven.lead!.cards[0].aside!.figures.map(nameOf)).toEqual([
      'the-lake-and-no-boat-a',
      'second',
    ]);
  });

  it('keeps the team drawing under the lede when no card is the coaching card', () => {
    const woven = weave([lead(['Decisions written down']), away('Away', [])], null);
    expect(woven.lead!.picture).toContain('how-i-lead');
    expect(woven.lead!.cards[0].picture).toBeNull();
    const none = weave([lead([COACH], false)], null);
    expect(none.lead!.cards[0].picture).toBeNull();
    expect(none.lead!.picture).toBeNull();
  });

  it('puts the hay-rack photo in the strip when the rabbits card is gone, and every photo between blocks in it', () => {
    const woven = weave(
      [lead([]), away('Away', ['Food'], ['couple-one', RABBITS.photo, 'stranger'])],
      '<p>Credit.</p>'
    );
    expect(woven.away!.strip!.photos.map((photo) => nameOf(photo.figure))).toEqual([
      'couple-one',
      RABBITS.photo,
      'stranger',
    ]);
    expect(woven.away!.strip!.credit).toBe('<p>Credit.</p>');
    expect(woven.credit).toBeNull();
  });

  it('hands the credit back when there is no strip, and a later panel through as the document has it', () => {
    const woven = weave([lead([]), away('Away', ['Food']), '<h2>More</h2>'], '<p>Credit.</p>');
    expect(woven.away!.strip).toBeNull();
    expect(woven.credit).toBe('<p>Credit.</p>');
    expect(woven.more).toEqual(['<h2>More</h2>']);
    expect(weave([], null)).toEqual({ lead: null, away: null, more: [], credit: null });
  });

  it('reads a panel with no blocks as its heading and words alone', () => {
    const woven = weave(['<h2>How I lead</h2><p>Only words.</p>'], null);
    expect(woven.lead).toEqual({
      heading: '<h2>How I lead</h2>',
      lede: '<p>Only words.</p>',
      cards: [],
      picture: null,
    });
  });
});
