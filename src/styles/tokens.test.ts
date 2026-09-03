import { describe, expect, it } from 'vitest';
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

function channel(hex: string, offset: number): number {
  const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;
  return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

/** Relative luminance, per WCAG 2.x. */
function luminance(hex: string): number {
  const h = hex.replace('#', '');
  return 0.2126 * channel(h, 0) + 0.7152 * channel(h, 2) + 0.0722 * channel(h, 4);
}

/** The WCAG contrast ratio between two hex colors, 1:1 up to 21:1. */
export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

// #region site-palette
// Contrast is measured, not eyeballed. The test reads the real tokens.css with
// ?raw and computes the WCAG ratio for every text and ground pair the site
// actually uses, so a palette change that fails AA fails the build instead of
// shipping (ADR-016).
describe('the site palette (ADR-016)', () => {
  it('body, muted and heading text clear AA on white and on the page ground', () => {
    for (const ground of ['color-surface', 'color-bg']) {
      expect(contrast(token('color-text'), token(ground))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token('color-text-muted'), token(ground))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token('color-heading'), token(ground))).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('faint labels clear AA on white, where they sit, and 3:1 on the ground', () => {
    expect(contrast(token('color-text-faint'), token('color-surface'))).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('color-text-faint'), token('color-bg'))).toBeGreaterThanOrEqual(3);
  });

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
