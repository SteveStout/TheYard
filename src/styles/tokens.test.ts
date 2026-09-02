import { describe, expect, it } from 'vitest';
import tokens from './tokens.css?raw';

/**
 * The phone drawer's palette (ADR-011 addendum) is dark, and "looks readable"
 * is not a measurement. This reads the sheet tokens straight from tokens.css
 * and holds every pair to WCAG AA: 4.5:1 for normal text, 3:1 for icons and
 * other graphics. A future shade change cannot slip under it.
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

describe('the phone drawer palette', () => {
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
