/**
 * WCAG contrast, measured (ADR-016). One implementation for the token tests
 * and for the swatches on the Colour and style page, so a figure on the page
 * and the figure the gate holds are the same arithmetic. No React in here.
 */

function channel(hex: string, offset: number): number {
  const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;
  return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

/** Relative luminance, per WCAG 2.x, of a six-digit hex colour. */
export function luminance(hex: string): number {
  const h = hex.replace('#', '');
  return 0.2126 * channel(h, 0) + 0.7152 * channel(h, 2) + 0.0722 * channel(h, 4);
}

/** The WCAG contrast ratio between two hex colours, 1:1 up to 21:1. */
export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Every `--name: #rrggbb;` in a stylesheet's text, in the order written. */
export function hexTokens(sheet: string): { name: string; hex: string }[] {
  return Array.from(sheet.matchAll(/(--[a-z0-9-]+):\s*(#[0-9a-fA-F]{6})\s*;/g), (match) => ({
    name: match[1],
    hex: match[2].toLowerCase(),
  }));
}
