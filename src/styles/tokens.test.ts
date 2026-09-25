import { describe, expect, it } from 'vitest';
import { contrast } from '../lib/contrast';
import tokens from './tokens.css?raw';

/**
 * The sidebar's palette (ADR-013; light since its addendum, dark in ADR-011's
 * first pass) is chosen by measurement, because "looks readable" is not one.
 * This reads the sheet tokens straight from tokens.css and holds every pair
 * to WCAG AA: 4.5:1 for normal text, 3:1 for icons and other graphics. A
 * future shade change cannot slip under it.
 */

function token(name: string): string {
  const match = tokens.match(new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{6})\\s*;`));
  if (!match) throw new Error(`tokens.css has no six-digit hex value for --${name}`);
  return match[1];
}

// #region site-palette
// Contrast is measured, not eyeballed. The test reads the real tokens.css with
// ?raw and computes the WCAG ratio for every text and ground pair the site
// actually uses, so a palette change that fails AA fails the build instead of
// shipping (ADR-016).
describe('the site palette (ADR-016)', () => {
  it('body, muted and heading text clear AA on white and on the page ground', () => {
    // The ground's darkest stop too, since the ribbons (ADR: The glass look, the addendum on the ribbon ground).
    for (const ground of ['color-surface', 'color-bg', 'color-ground-left']) {
      expect(contrast(token('color-text'), token(ground))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token('color-text-muted'), token(ground))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token('color-heading'), token(ground))).toBeGreaterThanOrEqual(4.5);
    }
  });

  // This test used to allow the faint colour 3:1 on the page ground, on the
  // reasoning that faint labels sit on white "where they sit". The stylesheet
  // did not agree: AuctionCountdown paints an ended auction in the faint colour
  // at 13px on the page ground, which is normal text and needs 4.5, and axe
  // found it at 3.82 (ADR-042). The exemption was the defect, not the colour,
  // so it is gone: a token used for text clears AA on every ground this site
  // puts it on, and if a future label really is large text it can say so with
  // its own assertion rather than by lowering everybody's floor.
  it('faint labels clear AA on white and on the page ground, not 3:1 on either', () => {
    for (const ground of ['color-surface', 'color-bg', 'color-ground-left']) {
      expect(contrast(token('color-text-faint'), token(ground))).toBeGreaterThanOrEqual(4.5);
    }
  });

  // The two lines of the activity graph are graphics, not text, so WCAG
  // 1.4.11 asks 3:1 against what they are drawn on, and they are drawn on
  // white inside a card on the page ground (ADR: Site activity, and the line
  // an address does not cross).
  it('the two store colours clear 3:1 on white and on the page ground, and are told apart', () => {
    for (const ground of ['color-surface', 'color-bg']) {
      expect(contrast(token('color-store-sql'), token(ground))).toBeGreaterThanOrEqual(3);
      expect(contrast(token('color-store-cosmos'), token(ground))).toBeGreaterThanOrEqual(3);
    }
    expect(token('color-store-sql')).not.toBe(token('color-store-cosmos'));
  });

  // A chart's series are graphics too, and they are identities: the status
  // colours are for states, and a series that takes one reads as an alarm
  // (ADR: The Admin tab, as a product, the addendum on the traffic card in plain words).
  it('the series colours clear 3:1 on white and on the page ground, are told apart, and are no status colour', () => {
    const series = [token('color-series-1'), token('color-series-2'), token('color-series-3')];
    for (const line of series) {
      for (const ground of ['color-surface', 'color-bg']) {
        expect(contrast(line, token(ground))).toBeGreaterThanOrEqual(3);
      }
      for (const status of ['color-success', 'color-warning', 'color-danger', 'color-live']) {
        expect(line.toLowerCase()).not.toBe(token(status).toLowerCase());
      }
    }
    expect(new Set(series).size).toBe(3);
    // Dark green beside bright teal is one family told apart by light and dark, so the gap is held too.
    expect(contrast(series[0], series[1])).toBeGreaterThanOrEqual(3);
  });

  // #region teal-and-gold
  // Teal fills, dark green draws, gold trims (ADR-016, the addendum on teal,
  // dark green and gold). Every pairing the site makes with them, measured.
  it('white reads on every teal and on the dark green, which is what the header and the chips put it on', () => {
    for (const ground of [
      'color-accent',
      'color-accent-hover',
      'color-teal-deep',
      'color-teal-header',
      'color-green-dark',
    ]) {
      expect(contrast(token('color-on-accent'), token(ground))).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('the deep teal reads as a title on white, and the accent as a link on the page ground', () => {
    expect(contrast(token('color-teal-deep'), token('color-surface'))).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('color-accent'), token('color-bg'))).toBeGreaterThanOrEqual(4.5);
  });

  it('gold light is safe as text on both ends of the header gradient, and the brand mark is that gold', () => {
    for (const end of ['color-green-dark', 'color-teal-header']) {
      expect(contrast(token('color-gold-light'), token(end))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token('color-header-text-muted'), token(end))).toBeGreaterThanOrEqual(4.5);
    }
    expect(token('color-brand-mark')).toBe(token('color-gold-light'));
    expect(token('color-header')).toBe(token('color-teal-header'));
  });

  it('gold is never text on white: it is under 3:1 there, which is why it is only ever trim', () => {
    // Not a floor to clear. A record of why the rule exists, held so that a
    // gold deepened until it could be text is noticed, because beside the
    // amber warning that gold reads as a warning.
    for (const gold of ['color-gold', 'color-gold-light']) {
      expect(contrast(token(gold), token('color-surface'))).toBeLessThan(3);
      expect(token(gold)).not.toBe(token('color-warning'));
    }
  });

  it('a plain tile is not the status green: the deep teal is told apart from it, and the accent is not', () => {
    // The accent teal sits 1.09 from the status green, so a plain tile wears the deep teal.
    expect(contrast(token('color-accent'), token('color-success'))).toBeLessThan(1.2);
    expect(contrast(token('color-teal-deep'), token('color-success'))).toBeGreaterThan(1.5);
  });
  // #endregion teal-and-gold

  // #region glass
  // Words are read against the WORST thing behind them, not against plain
  // white (ADR: The glass look): the darkest stroke of the watermark at its
  // strength, seen bare on the page ground, and seen through a glass panel.
  it('every text colour clears AA over the watermark at its worst, bare and through a panel', () => {
    const number = (name: string) => {
      const match = tokens.match(new RegExp(`--${name}:\\s*([0-9.]+)\\s*;`));
      if (!match) throw new Error(`tokens.css has no number for --${name}`);
      return Number(match[1]);
    };
    const glass = tokens.match(/--glass-bg:\s*rgba\(255, 255, 255, ([0-9.]+)\)/);
    if (!glass) throw new Error('tokens.css should state --glass-bg as white at a share');
    // The glass is transparent since the operator's look (0.42, from 0.66), and
    // thinner again since the tweaks pass (0.30); a phone's is a touch fuller, and
    // the desk's, the thinner, is the one held below.
    const phone = tokens.match(/--glass-bg-phone:\s*rgba\(255, 255, 255, ([0-9.]+)\)/);
    if (!phone) throw new Error('tokens.css should state --glass-bg-phone as white at a share');
    expect(Number(glass[1])).toBe(0.3);
    expect(Number(phone[1])).toBeGreaterThanOrEqual(Number(glass[1]));
    const rgb = (hex: string) => [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16));
    const hex = (parts: number[]) =>
      `#${parts.map((part) => Math.round(part).toString(16).padStart(2, '0')).join('')}`;
    const over = (top: string, bottom: string, share: number) =>
      hex(rgb(top).map((part, index) => part * share + rgb(bottom)[index] * (1 - share)));

    // The watermark's darkest ink is the teal of its rings: the rows are the lighter teal and the mark is gold.
    const stroke = over(token('color-accent'), token('color-bg'), number('watermark-opacity'));
    const throughPanel = over('#ffffff', stroke, Number(glass[1]));

    // On the bare ground the quiet colours are not used: muted and faint
    // words always sit inside a panel, and glass.spec.ts holds that against
    // the rendered page. Everything else that is ever a word on the ground is here.
    for (const name of [
      'color-heading',
      'color-text',
      'color-accent',
      'color-teal-deep',
      'color-success',
      'color-danger',
    ]) {
      expect(contrast(token(name), stroke)).toBeGreaterThanOrEqual(4.5);
    }
    for (const name of [
      'color-heading',
      'color-text',
      'color-text-muted',
      'color-text-faint',
      'color-accent',
      'color-teal-deep',
    ]) {
      expect(contrast(token(name), throughPanel)).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('is frosted: what is behind a panel reads as colour, not as shapes (Steve, 2026-09-25)', () => {
    const frost = tokens.match(/--glass-filter:\s*blur\((\d+)px\)\s*saturate\(([0-9.]+)\)/);
    if (!frost)
      throw new Error('tokens.css should state --glass-filter as a blur and a saturation');
    // Deeper in the tweaks pass: 28 px and 1.6, where it was 20 px and 1.5.
    expect(Number(frost[1])).toBe(28);
    expect(Number(frost[2])).toBe(1.6);
    expect(tokens).toContain('inset 0 0 40px rgba(255, 255, 255, 0.18)');
  });

  // #region worst-case-pairs
  // The worst case the tweaks pass named (B1): the secondary ink over the 30 per
  // cent glass where the glass lies straight over a ribbon's teal stop and its
  // gold stop, blended at the glass's share. The ink darkened until both held
  // 4.5 (#4d515a read 4.31 and 4.40); the glass's share did not move back. One
  // grey for secondary text: the faint ink and the sidebar's muted ink are it.
  it('the secondary ink holds 4.5 over the 30 per cent glass on the ribbons teal and gold stops', () => {
    const glass = tokens.match(/--glass-bg:\s*rgba\(255, 255, 255, ([0-9.]+)\)/);
    if (!glass) throw new Error('tokens.css should state --glass-bg as white at a share');
    const share = Number(glass[1]);
    const hex = (parts: number[]) =>
      `#${parts.map((part) => Math.round(part).toString(16).padStart(2, '0')).join('')}`;
    const through = (stop: number[]) => hex(stop.map((part) => 255 * share + part * (1 - share)));
    for (const stop of [
      [95, 179, 168],
      [201, 162, 74],
    ]) {
      expect(contrast(token('color-text-muted'), through(stop))).toBeGreaterThanOrEqual(4.5);
    }
    expect(token('color-text-faint')).toBe(token('color-text-muted'));
    expect(token('color-sheet-text-muted')).toBe(token('color-text-muted'));
  });

  // The document dialog (B1b): the sheet nearly clear, the reading panel frosted,
  // and body text on the reading panel over the same two stops holds 4.5.
  it('a document reads on a frosted panel inside a clear sheet', () => {
    expect(tokens).toContain('--dialog-sheet-bg: rgba(255, 255, 255, 0.1);');
    expect(tokens).toContain('--dialog-sheet-filter: blur(6px);');
    expect(tokens).toContain('--dialog-page-filter: blur(28px) saturate(1.5);');
    const page = tokens.match(/--dialog-page-bg:\s*rgba\(255, 255, 255, ([0-9.]+)\)/);
    if (!page) throw new Error('tokens.css should state --dialog-page-bg as white at a share');
    const share = Number(page[1]);
    expect(share).toBe(0.78);
    const hex = (parts: number[]) =>
      `#${parts.map((part) => Math.round(part).toString(16).padStart(2, '0')).join('')}`;
    for (const stop of [
      [95, 179, 168],
      [201, 162, 74],
    ]) {
      const ground = hex(stop.map((part) => 255 * share + part * (1 - share)));
      for (const name of ['color-text', 'color-text-muted', 'color-heading']) {
        expect(contrast(token(name), ground)).toBeGreaterThanOrEqual(4.5);
      }
    }
  });
  // #endregion worst-case-pairs

  it('a panel turns solid where a blur is not available or not wanted', () => {
    // Three fallbacks, each turning the panel's ground solid: no backdrop-filter,
    // a reader who asked for less transparency, and forced colours.
    const solid = (condition: RegExp) => {
      const at = tokens.search(condition);
      expect(at).toBeGreaterThan(-1);
      return tokens.slice(at, at + 220);
    };
    expect(solid(/@supports not \(\(backdrop-filter/)).toContain('--glass-bg: #ffffff;');
    expect(solid(/@media \(prefers-reduced-transparency: reduce\)/)).toContain(
      '--glass-bg: #ffffff;'
    );
    expect(solid(/@media \(forced-colors: active\)/)).toContain('--glass-bg: Canvas;');
  });
  // #endregion glass

  it('actions read both ways: white on the accent, and the accent as link text on white', () => {
    expect(contrast(token('color-on-accent'), token('color-accent'))).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('color-accent'), token('color-surface'))).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('color-accent'), token('color-accent-soft'))).toBeGreaterThanOrEqual(3);
  });

  it('the status colours clear AA on their own soft grounds and on white', () => {
    // The bid panel puts color-success on color-success-soft ("you are the
    // high bidder") and color-danger on color-danger-soft ("someone outbid
    // you"), and the card chips put white on the strong shade of each. Four
    // pairs, all normal text.
    for (const status of ['success', 'danger']) {
      expect(
        contrast(token(`color-${status}`), token(`color-${status}-soft`))
      ).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token(`color-${status}`), token('color-surface'))).toBeGreaterThanOrEqual(
        4.5
      );
      expect(contrast(token('color-on-accent'), token(`color-${status}`))).toBeGreaterThanOrEqual(
        4.5
      );
    }
  });

  // #region composed-pairs
  // The two pairs axe found and this file did not (ADR-035). Neither is
  // exotic. They are pairs composed by a stylesheet rather than listed by a
  // person, which is the category an enumerated test cannot cover on its own.
  it('the pairs the stylesheet composes clear AA, not only the ones listed here', () => {
    // The "Reserve not met" chip: muted text on the neutral chip ground.
    expect(contrast(token('color-text-muted'), token('color-neutral-soft'))).toBeGreaterThanOrEqual(
      4.5
    );
    // The live countdown on a vehicle: success green on the page ground.
    expect(contrast(token('color-success'), token('color-bg'))).toBeGreaterThanOrEqual(4.5);
  });
  // #endregion composed-pairs

  it('the header text clears AA on the header', () => {
    expect(contrast(token('color-header-text'), token('color-header'))).toBeGreaterThanOrEqual(4.5);
    expect(
      contrast(token('color-header-text-muted'), token('color-header'))
    ).toBeGreaterThanOrEqual(4.5);
  });
});
// #endregion site-palette

describe('the controls (1.0.3.9)', () => {
  it('have one height per kind on a desk, and every kind is a thumb on a phone', () => {
    expect(tokens).toContain('--pill-height: 34px;');
    expect(tokens).toContain('--control-height: 40px;');
    expect(tokens).toContain('--control-height-lg: 48px;');
    expect(tokens).toContain('--touch-target: 44px;');
    const phone = tokens.slice(
      tokens.indexOf('#region controls-phone'),
      tokens.indexOf('#endregion controls-phone')
    );
    expect(phone).toContain('--pill-height: var(--touch-target);');
    expect(phone).toContain('--control-height: var(--touch-target);');
  });

  it('have one focus ring, one offset, and one inset for a control clipped by its box', () => {
    expect(tokens).toContain('--focus-ring: 2px solid var(--color-accent);');
    expect(tokens).toContain('--focus-ring-on-dark: 2px solid var(--color-gold-light);');
    expect(tokens).toContain('--focus-ring-offset: 2px;');
    expect(tokens).toContain('--focus-ring-inset: -2px;');
  });

  it('have one weight for a title, one for a badge, and one border and radius for an input', () => {
    expect(tokens).toContain('--title-weight: var(--weight-bold);');
    expect(tokens).toContain('--badge-weight: var(--weight-medium);');
    expect(tokens).toContain('--input-border: 1px solid var(--color-border-strong);');
    expect(tokens).toContain('--input-radius: var(--radius-md);');
  });
});

describe('the sidebar palette', () => {
  const grounds = ['color-sheet-bg', 'color-sheet-bg-raised'];

  it.each(grounds)('text and muted text clear AA for normal text on %s', (ground) => {
    expect(contrast(token('color-sheet-text'), token(ground))).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('color-sheet-text-muted'), token(ground))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(grounds)('icons and the focus ring clear AA for graphics on %s', (ground) => {
    expect(contrast(token('color-sheet-icon'), token(ground))).toBeGreaterThanOrEqual(3);
    expect(contrast(token('color-sheet-icon-active'), token(ground))).toBeGreaterThanOrEqual(3);
    expect(contrast(token('color-sheet-focus'), token(ground))).toBeGreaterThanOrEqual(3);
  });

  it('measures the way WCAG does: white on black is 21:1', () => {
    expect(contrast('#ffffff', '#000000')).toBeCloseTo(21, 1);
  });
});

// #region code-palette
/**
 * The code theme (ADR: Code that reads like code). Code sits on
 * --color-surface-muted inside a document, so that is the ground every token
 * colour is measured against, and 4.5 is the floor because a keyword is normal
 * text at 0.9em. The second assertion is the one a palette change actually
 * breaks: six colours that all clear AA and are impossible to tell apart is a
 * theme that passed a test and failed a reader.
 */
describe('the code theme', () => {
  const codeTokens = [
    'color-code-keyword',
    'color-code-type',
    'color-code-string',
    'color-code-number',
    'color-code-comment',
    'color-code-meta',
  ];

  it('every code colour clears AA on the ground code sits on', () => {
    for (const name of codeTokens) {
      expect(contrast(token(name), token('color-surface-muted'))).toBeGreaterThanOrEqual(4.5);
    }

    // The code itself is darker than the prose around it, because the ground it
    // sits on is a light gray rather than white and 4.5 on gray reads thin. AAA
    // is the floor for the one colour most of a sample is written in.
    expect(contrast(token('color-code-text'), token('color-surface-muted'))).toBeGreaterThanOrEqual(
      7
    );
  });

  it('no two code colours are the same, and none of them is the prose colour', () => {
    const values = codeTokens.map(token);
    expect(new Set(values).size).toBe(codeTokens.length);
    for (const value of values) {
      expect(value).not.toBe(token('color-text'));
    }
  });
});
// #endregion code-palette
