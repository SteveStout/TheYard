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

/**
 * Every colour token in a stylesheet's text that comes to a six-digit hex, in
 * the order written: `--name: #rrggbb;`, and `--name: var(--other);` read
 * through to the hex the other one comes to, however many steps away (the
 * styling pass of 25 September: a token that repeats another's value is
 * written as that token). The first value a name is given is the token; the
 * fallback blocks under the sheet give some names a second. A name whose value
 * is anything else (a color-mix, an rgba) is left out, and so is a var() that
 * never reaches a hex, a loop included.
 */
export function hexTokens(sheet: string): { name: string; hex: string }[] {
  // The first value a name is given is the token, whatever it is: --glass-bg's
  // token is an rgba (left out), not the solid white a fallback gives it later.
  const written = new Map<string, string>();
  const text = sheet.replace(/\/\*[\s\S]*?\*\//g, ' ');
  for (const match of text.matchAll(/(--[a-z0-9-]+):\s*([^;{}]+);/g)) {
    if (written.has(match[1])) continue;
    const value = match[2].trim();
    const alias = /^var\(\s*(--[a-z0-9-]+)\s*\)$/.exec(value);
    written.set(
      match[1],
      alias ? alias[1] : /^#[0-9a-fA-F]{6}$/.test(value) ? value.toLowerCase() : ''
    );
  }
  const resolve = (name: string, seen: Set<string>): string | undefined => {
    const value = written.get(name);
    if (value === undefined || value === '' || seen.has(name)) return undefined;
    return value.startsWith('#') ? value : resolve(value, new Set(seen).add(name));
  };
  return Array.from(written.keys()).flatMap((name) => {
    const hex = resolve(name, new Set());
    return hex === undefined ? [] : [{ name, hex }];
  });
}
